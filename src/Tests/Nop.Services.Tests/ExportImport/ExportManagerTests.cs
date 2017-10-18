using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Tax;
using Nop.Core.Infrastructure;
using Nop.Services.Authentication;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.ExportImport;
using Nop.Services.ExportImport.Help;
using Nop.Services.Media;
using Nop.Services.Messages;
using Nop.Services.Shipping.Date;
using Nop.Services.Stores;
using Nop.Services.Tax;
using Nop.Services.Vendors;
using Nop.Tests;
using NUnit.Framework;
using OfficeOpenXml;
using Rhino.Mocks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Domain.Vendors;
using Nop.Services.Localization;
using Nop.Services.Seo;

namespace Nop.Services.Tests.ExportImport
{
    [TestFixture]
    public class ExportManagerTests : ServiceTest
    {

        private IPictureService _pictureService;
        private IExportManager _exportManager;
        private IGenericAttributeService _genericAttributeService;
        private IAuthenticationService _authenticationService;
        private ILocalizationService _localizationService;
        private IWorkContext _workContext;

        [SetUp]
        public new void SetUp()
        {
            _pictureService = MockRepository.GenerateMock<IPictureService>();
            _authenticationService = MockRepository.GenerateMock<IAuthenticationService>();
            _localizationService = MockRepository.GenerateMock<ILocalizationService>();
            _workContext = MockRepository.GenerateMock<IWorkContext>();

            var nopEngine = MockRepository.GenerateMock<NopEngine>();

            var serviceProvider = MockRepository.GenerateMock<IServiceProvider>();
            nopEngine.Expect(x => x.ServiceProvider).Return(serviceProvider);

            var urlRecordService = MockRepository.GenerateMock<IUrlRecordService>();

            _genericAttributeService = MockRepository.GenerateMock<IGenericAttributeService>();
            _genericAttributeService.Expect(p => p.GetAttributesForEntity(1, "Customer"))
                .Return(new List<GenericAttribute>
                {
                    new GenericAttribute
                    {
                        EntityId = 1,
                        Key = "manufacturer-advanced-mode",
                        KeyGroup = "Customer",
                        StoreId = 0,
                        Value = "true"
                    }
                });

            serviceProvider.Expect(x => x.GetRequiredService(typeof(IGenericAttributeService))).Return(_genericAttributeService);
            serviceProvider.Expect(x => x.GetRequiredService(typeof(IUrlRecordService))).Return(urlRecordService);
            serviceProvider.Expect(x => x.GetRequiredService(typeof(ILocalizationService))).Return(_localizationService);
            serviceProvider.Expect(x => x.GetRequiredService(typeof(IWorkContext))).Return(_workContext);

            var httpContextAccessor = MockRepository.GenerateMock<IHttpContextAccessor>();
            serviceProvider.Expect(x => x.GetRequiredService(typeof(IHttpContextAccessor))).Return(httpContextAccessor);

            EngineContext.Replace(nopEngine);


            var picture = new Picture
            {
                Id = 1,
                SeoFilename = "picture"
            };

            _authenticationService.Expect(p => p.GetAuthenticatedCustomer()).Return(GetTestCustomer());
            _pictureService.Expect(p => p.GetPictureById(1)).Return(picture);
            _pictureService.Expect(p => p.GetThumbLocalPath(picture)).Return(@"c:\temp\picture.png");
            _pictureService.Expect(p => p.GetPicturesByProductId(1, 3)).Return(new List<Picture> {picture});

            _exportManager = new TestExportManager(_pictureService, _genericAttributeService, _workContext);
        }

        #region Utilities

        protected PropertyManager<T> GetPropertyManager<T>(ExcelWorksheet worksheet)
        {
            //the columns
            var properties = ImportManager.GetPropertiesByExcelCells<T>(worksheet);

            return new PropertyManager<T>(properties);
        }

        protected ExcelWorksheet GetWorksheets(byte[] excelData)
        {
            var stream = new MemoryStream(excelData);
            var xlPackage = new ExcelPackage(stream);

            // get the first worksheet in the workbook
            var worksheet = xlPackage.Workbook.Worksheets.FirstOrDefault();
            if (worksheet == null)
                throw new NopException("No worksheet found");

            return worksheet;
        }

        protected static T PropertiesShouldEqual<T, Tp>(T actual, PropertyManager<Tp> manager, IDictionary<string, string> replacePairs, params string[] filter)
        {
            var objectProperties = typeof(T).GetProperties();
            foreach (var property in manager.GetProperties)
            {
                if (filter.Contains(property.PropertyName))
                    continue;

                var objectProperty = replacePairs.ContainsKey(property.PropertyName)
                    ? objectProperties.FirstOrDefault(p => p.Name == replacePairs[property.PropertyName])
                    : objectProperties.FirstOrDefault(p => p.Name == property.PropertyName);

                if (objectProperty == null)
                    continue;

                var objectPropertyValue = objectProperty.GetValue(actual);
                objectPropertyValue = objectPropertyValue ?? string.Empty;

                if (objectProperty.PropertyType == typeof(Guid))
                {
                    objectPropertyValue = objectPropertyValue.ToString();
                }

                if (objectProperty.PropertyType.IsEnum)
                {
                    objectPropertyValue = (int)objectPropertyValue;
                }

                if (objectProperty.PropertyType == typeof(DateTime))
                {
                    objectPropertyValue = ((DateTime)objectPropertyValue).ToOADate();
                }

                Assert.AreEqual(objectPropertyValue, property.PropertyValue, $"The property \"{typeof(T).Name}.{property.PropertyName}\" of these objects is not equal");
            }

            return actual;
        }

        protected T AreAllObjectPropertiesPresent<T>(T obj, PropertyManager<T> manager, params string[] filters)
        {
            foreach (var propertyInfo in typeof(T).GetProperties())
            {
                if (filters.Contains(propertyInfo.Name))
                    continue;

                if (manager.GetProperties.Any(p => p.PropertyName == propertyInfo.Name))
                    continue;

                Assert.Fail("The property \"{0}.{1}\" no present on excel file", typeof(T).Name, propertyInfo.Name);
            }

            return obj;
        }

        protected Address GetTestBillingAddress()
        {
            return new Address
            {
                FirstName = "FirstName 1",
                LastName = "LastName 1",
                Email = "Email 1",
                Company = "Company 1",
                City = "City 1",
                Address1 = "Address1a",
                Address2 = "Address1a",
                ZipPostalCode = "ZipPostalCode 1",
                PhoneNumber = "PhoneNumber 1",
                FaxNumber = "FaxNumber 1",
                CreatedOnUtc = new DateTime(2010, 01, 01),
                Country = GetTestCountry()
            };
        }

        protected Address GetTestShippingAddress()
        {
            return new Address
            {
                FirstName = "FirstName 2",
                LastName = "LastName 2",
                Email = "Email 2",
                Company = "Company 2",
                City = "City 2",
                Address1 = "Address2a",
                Address2 = "Address2b",
                ZipPostalCode = "ZipPostalCode 2",
                PhoneNumber = "PhoneNumber 2",
                FaxNumber = "FaxNumber 2",
                CreatedOnUtc = new DateTime(2010, 01, 01),
                Country = GetTestCountry()
            };
        }

        protected Country GetTestCountry()
        {
            return new Country
            {
                Name = "United States",
                AllowsBilling = true,
                AllowsShipping = true,
                TwoLetterIsoCode = "US",
                ThreeLetterIsoCode = "USA",
                NumericIsoCode = 1,
                SubjectToVat = true,
                Published = true,
                DisplayOrder = 1
            };
        }

        protected Customer GetTestCustomer()
        {
            return new Customer
            {
                Id = 1,
                CustomerGuid = Guid.NewGuid(),
                AdminComment = "some comment here",
                Active = true,
                Deleted = false,
                CreatedOnUtc = new DateTime(2010, 01, 01),
            };
        }

        #endregion

        #region Test export to excel

        [Test]
        public void can_export_orders_xlsx()
        {
            var orderGuid = Guid.NewGuid();
            var billingAddress = GetTestBillingAddress();
            var shippingAddress = GetTestShippingAddress();

            var orders = new List<Order>
            {
                new Order
                {
                    Id = 1,
                    OrderGuid = orderGuid,
                    CustomerId = 1,
                    Customer = GetTestCustomer(),
                    StoreId = 1,
                    OrderStatus = OrderStatus.Complete,
                    ShippingStatus = ShippingStatus.Shipped,
                    PaymentStatus = PaymentStatus.Paid,
                    PaymentMethodSystemName = "PaymentMethodSystemName1",
                    CustomerCurrencyCode = "RUR",
                    CurrencyRate = 1.1M,
                    CustomerTaxDisplayType = TaxDisplayType.ExcludingTax,
                    VatNumber = "123456789",
                    OrderSubtotalInclTax = 2.1M,
                    OrderSubtotalExclTax = 3.1M,
                    OrderSubTotalDiscountInclTax = 4.1M,
                    OrderSubTotalDiscountExclTax = 5.1M,
                    OrderShippingInclTax = 6.1M,
                    OrderShippingExclTax = 7.1M,
                    PaymentMethodAdditionalFeeInclTax = 8.1M,
                    PaymentMethodAdditionalFeeExclTax = 9.1M,
                    TaxRates = "1,3,5,7",
                    OrderTax = 10.1M,
                    OrderDiscount = 11.1M,
                    OrderTotal = 12.1M,
                    RefundedAmount = 13.1M,
                    CheckoutAttributeDescription = "CheckoutAttributeDescription1",
                    CheckoutAttributesXml = "CheckoutAttributesXml1",
                    CustomerLanguageId = 14,
                    AffiliateId = 15,
                    CustomerIp = "CustomerIp1",
                    AllowStoringCreditCardNumber = true,
                    CardType = "Visa",
                    CardName = "John Smith",
                    CardNumber = "4111111111111111",
                    MaskedCreditCardNumber = "************1111",
                    CardCvv2 = "123",
                    CardExpirationMonth = "12",
                    CardExpirationYear = "2010",
                    AuthorizationTransactionId = "AuthorizationTransactionId1",
                    AuthorizationTransactionCode = "AuthorizationTransactionCode1",
                    AuthorizationTransactionResult = "AuthorizationTransactionResult1",
                    CaptureTransactionId = "CaptureTransactionId1",
                    CaptureTransactionResult = "CaptureTransactionResult1",
                    SubscriptionTransactionId = "SubscriptionTransactionId1",
                    PaidDateUtc = new DateTime(2010, 01, 01),
                    CustomValuesXml = "<test>test</test>",
                    BillingAddress = billingAddress,
                    ShippingAddress = shippingAddress,
                    ShippingMethod = "ShippingMethod1",
                    ShippingRateComputationMethodSystemName = "ShippingRateComputationMethodSystemName1",
                    Deleted = false,
                    CreatedOnUtc = new DateTime(2010, 01, 04)
                }
            };
            var excelData = _exportManager.ExportOrdersToXlsx(orders);
            var worksheet = GetWorksheets(excelData);
            var manager = GetPropertyManager<Order>(worksheet);

            manager.ReadFromXlsx(worksheet, 2);

            var replacePairse = new Dictionary<string, string>
                {
                    { "OrderId", "Id" },
                    { "OrderStatusId", "OrderStatus" },
                    { "PaymentStatusId", "PaymentStatus" },
                    { "ShippingStatusId", "ShippingStatus" },
                    { "ShippingPickUpInStore", "PickUpInStore" }
                };

            var order = orders.First();

            var ignore = new List<string>();
            ignore.AddRange(replacePairse.Values);

            //not exported fields
            ignore.AddRange(new[]
            {
                "BillingAddressId", "ShippingAddressId", "PickupAddressId", "CustomerTaxDisplayTypeId",
                "RewardPointsHistoryEntryId", "CheckoutAttributeDescription", "CheckoutAttributesXml",
                "CustomerLanguageId", "CustomerIp", "AllowStoringCreditCardNumber", "CardType", "CardName",
                "CardNumber", "MaskedCreditCardNumber", "CardCvv2", "CardExpirationMonth", "CardExpirationYear",
                "AuthorizationTransactionId", "AuthorizationTransactionCode", "AuthorizationTransactionResult",
                "CaptureTransactionId", "CaptureTransactionResult", "SubscriptionTransactionId", "PaidDateUtc",
                "Deleted", "PickupAddress", "RedeemedRewardPointsEntry", "DiscountUsageHistory", "GiftCardUsageHistory",
                "OrderNotes", "OrderItems", "Shipments", "OrderStatus", "PaymentStatus", "ShippingStatus ",
                "CustomerTaxDisplayType", "TaxRatesDictionary", "CustomOrderNumber"
            });

            //fields tested individually
            ignore.AddRange(new[]
            {
               "Customer", "BillingAddress", "ShippingAddress"
            });

            AreAllObjectPropertiesPresent(order, manager, ignore.ToArray());
            PropertiesShouldEqual(order, manager, replacePairse);

            var addressFilds = new List<string>
            {
                "FirstName",
                "LastName",
                "Email",
                "Company",
                "Country",
                "StateProvince",
                "City",
                "Address1",
                "Address2",
                "ZipPostalCode",
                "PhoneNumber",
                "FaxNumber"
            };

            const string billingPatern = "Billing";
            replacePairse = addressFilds.ToDictionary(p => billingPatern + p, p => p);
            PropertiesShouldEqual(billingAddress, manager, replacePairse, "CreatedOnUtc", "BillingCountry");
            manager.GetProperties.First(p => p.PropertyName == "BillingCountry").PropertyValue.ShouldEqual(billingAddress.Country.Name);

            const string shippingPatern = "Shipping";
            replacePairse = addressFilds.ToDictionary(p => shippingPatern + p, p => p);
            PropertiesShouldEqual(shippingAddress, manager, replacePairse, "CreatedOnUtc", "ShippingCountry");
            manager.GetProperties.First(p => p.PropertyName == "ShippingCountry").PropertyValue.ShouldEqual(shippingAddress.Country.Name);
        }

        [Test]
        public void can_export_manufacturers_xlsx()
        {
            var manufacturers = new List<Manufacturer>
            {
                new Manufacturer
                {
                    Id = 1,
                    Name = "TestManufacturer",
                    Description = "TestDescription",
                    ManufacturerTemplateId = 1,
                    MetaKeywords = "MetaKeywords",
                    MetaDescription = "MetaDescription",
                    MetaTitle = "MetaTitle",
                    PictureId = 1,
                    PageSize = 15,
                    AllowCustomersToSelectPageSize = true,
                    PageSizeOptions = "5,10,15",
                    PriceRanges = "",
                    Published = true,
                    DisplayOrder = 1
                }
            };

            var excelData = _exportManager.ExportManufacturersToXlsx(manufacturers);
            var worksheet = GetWorksheets(excelData);
            var manager = GetPropertyManager<Manufacturer>(worksheet);

            manager.ReadFromXlsx(worksheet, 2);

            var manufacturer = manufacturers.First();

            var ignore = new List<string> { "Picture", "PictureId", "SubjectToAcl", "LimitedToStores", "Deleted", "CreatedOnUtc", "UpdatedOnUtc", "AppliedDiscounts" };

            AreAllObjectPropertiesPresent(manufacturer, manager, ignore.ToArray());
            PropertiesShouldEqual(manufacturer, manager, new Dictionary<string, string>());

            manager.GetProperties.First(p => p.PropertyName == "Picture").PropertyValue.ShouldEqual(@"c:\temp\picture.png");
        }

        [Test]
        public void can_export_customers_to_xlsx()
        {
            var customers = new List<Customer>
            {
                new Customer
                {
                    Active = true,
                    AffiliateId = 0,
                    CreatedOnUtc = new DateTime(2010, 01, 04),
                    CustomerGuid = Guid.NewGuid(),
                    Email = "test@test.com",
                    Username = "Test",
                    IsTaxExempt = true,
                    VendorId = 0
                }
            };

            var excelData = _exportManager.ExportCustomersToXlsx(customers);
            var worksheet = GetWorksheets(excelData);
            var manager = GetPropertyManager<Customer>(worksheet);

            manager.ReadFromXlsx(worksheet, 2);
            var customer = customers.First();

            var ignore = new List<string> {"Id", "ExternalAuthenticationRecords", "CustomerRoles", "ShoppingCartItems", "ReturnRequests", "BillingAddress", "ShippingAddress", "Addresses", "AdminComment", "EmailToRevalidate", "HasShoppingCartItems", "RequireReLogin", "FailedLoginAttempts", "CannotLoginUntilDateUtc", "Deleted", "IsSystemAccount", "SystemName", "LastIpAddress", "LastLoginDateUtc", "LastActivityDateUtc", "RegisteredInStoreId" };

            AreAllObjectPropertiesPresent(customer, manager, ignore.ToArray());
            PropertiesShouldEqual(customer, manager, new Dictionary<string, string>());
        }

        [Test]
        public void can_export_categories_to_xlsx()
        {
            var categories = new List<Category>
            {
                new Category
                {
                    Id = 1,
                    Name = "TestCategory",
                    Description = "TestDescription",
                    CategoryTemplateId = 1,
                    MetaKeywords = "TestMetaKeywords",
                    MetaDescription = "TestMetaDescription",
                    MetaTitle = "TestMetaTitle",
                    ParentCategoryId = 0,
                    PictureId = 1,
                    PageSize = 10,
                    AllowCustomersToSelectPageSize = true,
                    PageSizeOptions = "10;20;30",
                    PriceRanges = "100;200;300",
                    ShowOnHomePage = true,
                    IncludeInTopMenu = true,
                    Published = true,
                    DisplayOrder = 1
                }
            };

            var excelData = _exportManager.ExportCategoriesToXlsx(categories);
            var worksheet = GetWorksheets(excelData);
            var manager = GetPropertyManager<Category>(worksheet);

            manager.ReadFromXlsx(worksheet, 2);
            var category = categories.First();

            var ignore = new List<string> { "CreatedOnUtc", "Picture", "PictureId", "AppliedDiscounts", "UpdatedOnUtc", "SubjectToAcl", "LimitedToStores", "Deleted" };

            AreAllObjectPropertiesPresent(category, manager, ignore.ToArray());
            PropertiesShouldEqual(category, manager, new Dictionary<string, string>());

            manager.GetProperties.First(p => p.PropertyName == "Picture").PropertyValue.ShouldEqual(@"c:\temp\picture.png");
        }

        [Test]
        public void can_export_products_to_xlsx()
        {
            var replacePairse = new Dictionary<string, string>
            {
                { "ProductId", "Id" },
                { "ProductType", "ProductTypeId" },
                { "Vendor", "VendorId" },
                { "ProductTemplate", "ProductTemplateId" },
                { "DeliveryDate", "DeliveryDateId"},
                { "TaxCategory", "TaxCategoryId"},
                { "ManageInventoryMethod", "ManageInventoryMethodId" },
                { "ProductAvailabilityRange", "ProductAvailabilityRangeId" },
                { "LowStockActivity", "LowStockActivityId" },
                {"BackorderMode", "BackorderModeId" },
                {"BasepriceUnit", "BasepriceUnitId" },
                {"BasepriceBaseUnit", "BasepriceBaseUnitId" }
            };

            var products = new List<Product>
            {
                new Product
                {
                    Id = 1,
                    ProductTypeId = 1,
                    ParentGroupedProductId =0,
                    VisibleIndividually = true,
                    Name = "TestProduct",
                    ShortDescription = "TestShortDescription",
                    FullDescription = "TestFullDescription",
                    VendorId = 0,
                    ProductTemplateId = 1,
                    ShowOnHomePage = true,
                    MetaKeywords = "TestMetaKeywords",
                    MetaDescription = "TestMetaDescription",
                    MetaTitle = "TestMetaTitle",
                    AllowCustomerReviews = true,
                    Published = true,
                    Sku = "TestSku",
                    ManufacturerPartNumber = "TestManufacturerPartNumber",
                    Gtin = "TestGtin",
                    IsGiftCard = false,
                    GiftCardTypeId = (int)GiftCardType.Virtual,
                    OverriddenGiftCardAmount = 0,
                    RequireOtherProducts = false,
                    RequiredProductIds = "0",
                    AutomaticallyAddRequiredProducts = true,
                    IsDownload = false,
                    DownloadId = 0,
                    UnlimitedDownloads = true,
                    MaxNumberOfDownloads = 100,
                    DownloadActivationTypeId = (int)DownloadActivationType.WhenOrderIsPaid,
                    HasSampleDownload = false,
                    SampleDownloadId = 0,
                    HasUserAgreement = false,
                    UserAgreementText = string.Empty,
                    IsRecurring = false,
                    RecurringCycleLength = 1,
                    RecurringCyclePeriodId = (int)RecurringProductCyclePeriod.Years,
                    RecurringTotalCycles = 10,
                    IsRental = false,
                    RentalPriceLength = 1,
                    RentalPricePeriodId = (int)RentalPricePeriod.Years,
                    IsShipEnabled = true,
                    IsFreeShipping = true,
                    ShipSeparately = false,
                    AdditionalShippingCharge = 0,
                    DeliveryDateId = 0,
                    IsTaxExempt = false,
                    TaxCategoryId = 0,
                    IsTelecommunicationsOrBroadcastingOrElectronicServices = false,
                    ManageInventoryMethodId = (int) ManageInventoryMethod.DontManageStock,
                    ProductAvailabilityRangeId = 0,
                    UseMultipleWarehouses = false,
                    WarehouseId = 0,
                    StockQuantity = 100,
                    DisplayStockAvailability = true,
                    DisplayStockQuantity = true,
                    MinStockQuantity = 1,
                    LowStockActivityId = (int)LowStockActivity.Nothing,
                    NotifyAdminForQuantityBelow = 5,
                    BackorderModeId=(int)BackorderMode.NoBackorders,
                    AllowBackInStockSubscriptions = true,
                    OrderMinimumQuantity = 1,
                    OrderMaximumQuantity = 10,
                    AllowedQuantities = "1;5;10",
                    NotReturnable = true,
                    DisableBuyButton = true,
                    DisableWishlistButton = true,
                    AvailableForPreOrder = true,
                    PreOrderAvailabilityStartDateTimeUtc = new DateTime(2010, 01, 04),
                    CallForPrice = true,
                    Price = 40,
                    OldPrice = 50,
                    ProductCost = 40,
                    CustomerEntersPrice = true,
                    MinimumCustomerEnteredPrice = 40,
                    MaximumCustomerEnteredPrice = 60,
                    BasepriceEnabled = true,
                    BasepriceAmount = 40,
                    BasepriceBaseUnitId = 0,
                    BasepriceBaseAmount = 40,
                    BasepriceUnitId = 0,
                    MarkAsNew = true,
                    MarkAsNewStartDateTimeUtc = new DateTime(2010, 01, 04),
                    MarkAsNewEndDateTimeUtc = new DateTime(2020, 01, 04),
                    Weight = 10,
                    Length = 10,
                    Width = 10,
                    Height = 10,
                }
            };

            var ignore = new List<string> { "Categories", "Manufacturers", "ProductTags" };

            var excelData = _exportManager.ExportProductsToXlsx(products);
            var worksheet = GetWorksheets(excelData);
            var manager = GetPropertyManager<Product>(worksheet);

            manager.ReadFromXlsx(worksheet, 2);
            var product = products.First();
            
            AreAllObjectPropertiesPresent(product, manager, ignore.ToArray());
            PropertiesShouldEqual(product, manager, new Dictionary<string, string>());
        }

        #endregion
    }

    #region Nested classes

    internal class TestExportManager : ExportManager
    {
        public TestExportManager(IPictureService pictureService, IGenericAttributeService genericAttributeService, IWorkContext workContext)
        {
            this._storeService = MockRepository.GenerateMock<IStoreService>();
            this._productAttributeService = MockRepository.GenerateMock<IProductAttributeService>();
            this._vendorService = MockRepository.GenerateMock<IVendorService>();

            _vendorService.Expect(v => v.GetAllVendors(showHidden: true))
                .Return(new PagedList<Vendor>(new List<Vendor> {new Vendor()}, 0, 10));

            this._productTemplateService = MockRepository.GenerateMock<IProductTemplateService>();

            _productTemplateService.Expect(p => p.GetAllProductTemplates())
                .Return(new List<ProductTemplate> {new ProductTemplate()});

            this._dateRangeService = MockRepository.GenerateMock<IDateRangeService>();
            _dateRangeService.Expect(d => d.GetAllDeliveryDates()).Return(new List<DeliveryDate> {new DeliveryDate()});
            _dateRangeService.Expect(d => d.GetAllProductAvailabilityRanges()).Return(new List<ProductAvailabilityRange> {new ProductAvailabilityRange()});
            this._taxCategoryService = MockRepository.GenerateMock<ITaxCategoryService>();
            _taxCategoryService.Expect(t => t.GetAllTaxCategories()).Return(new List<TaxCategory> {new TaxCategory()});
            this._measureService = MockRepository.GenerateMock<IMeasureService>();
            _measureService.Expect(m => m.GetAllMeasureWeights()).Return(new List<MeasureWeight> {new MeasureWeight()});
            this._catalogSettings = new CatalogSettings();
            this._specificationAttributeService = MockRepository.GenerateMock<ISpecificationAttributeService>();
            this._orderSettings = new OrderSettings();
            this._categoryService = MockRepository.GenerateMock<ICategoryService>();
            _categoryService.Expect(c => c.GetProductCategoriesByProductId(1, true))
                .Return(new List<ProductCategory>());
            this._manufacturerService = MockRepository.GenerateMock<IManufacturerService>();
            _manufacturerService.Expect(m => m.GetProductManufacturersByProductId(1, true)).Return(new List<ProductManufacturer>());
            this._customerService = MockRepository.GenerateMock<ICustomerService>();
            this._workContext = workContext;
            this._newsLetterSubscriptionService = MockRepository.GenerateMock<INewsLetterSubscriptionService>();
            this._productEditorSettings = new ProductEditorSettings();
            this._customerAttributeFormatter = MockRepository.GenerateMock<ICustomerAttributeFormatter>();

            this._genericAttributeService = genericAttributeService;
            this._pictureService = pictureService;
        }

        protected override bool IgnoreExportPoductProperty(Func<ProductEditorSettings, bool> func)
        {
            return false;
        }

        protected override bool IgnoreExportCategoryProperty()
        {
            return false;
        }

        protected override bool IgnoreExportManufacturerProperty()
        {
            return false;
        }
    }

    #endregion
}
