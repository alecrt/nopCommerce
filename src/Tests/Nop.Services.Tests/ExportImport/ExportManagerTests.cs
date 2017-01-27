using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.Media;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Shipping;
using Nop.Core.Domain.Tax;
using Nop.Core.Infrastructure;
using Nop.Core.Infrastructure.DependencyManagement;
using Nop.Services.Authentication;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.ExportImport;
using Nop.Services.ExportImport.Help;
using Nop.Services.Helpers;
using Nop.Services.Localization;
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
using Nop.Web.Framework;
using Autofac;
using Nop.Services.Seo;

namespace Nop.Services.Tests.ExportImport
{
    [TestFixture]
    public class ExportManagerTests : ServiceTest
    {
        private ICategoryService _categoryService;
        private IManufacturerService _manufacturerService;
        private IProductAttributeService _productAttributeService;
        private IPictureService _pictureService;
        private INewsLetterSubscriptionService _newsLetterSubscriptionService;
        private IExportManager _exportManager;
        private IStoreService _storeService;
        private ProductEditorSettings _productEditorSettings;
        private IWorkContext _workContext;
        private IVendorService _vendorService;
        private IProductTemplateService _productTemplateService;
        private IDateRangeService _dateRangeService;
        private ITaxCategoryService _taxCategoryService;
        private IMeasureService _measureService;
        private CatalogSettings _catalogSettings;
        private IGenericAttributeService _genericAttributeService;
        private ICustomerAttributeFormatter _customerAttributeFormatter;

        private HttpContextBase _httpContext;
        private ICustomerService _customerService;
        private IStoreContext _storeContext;
        private IAuthenticationService _authenticationService;
        private ILanguageService _languageService;
        private ICurrencyService _currencyService;
        private TaxSettings _taxSettings;
        private CurrencySettings _currencySettings;
        private LocalizationSettings _localizationSettings;
        private IUserAgentHelper _userAgentHelper;
        private IStoreMappingService _storeMappingService;

        [SetUp]
        public new void SetUp()
        {
            _storeService = MockRepository.GenerateMock<IStoreService>();
            _categoryService = MockRepository.GenerateMock<ICategoryService>();
            _manufacturerService = MockRepository.GenerateMock<IManufacturerService>();
            _productAttributeService = MockRepository.GenerateMock<IProductAttributeService>();
            _pictureService = MockRepository.GenerateMock<IPictureService>();

            _httpContext = MockRepository.GenerateMock<HttpContextBase>();
            _customerService = MockRepository.GenerateMock<ICustomerService>();
            _storeContext = MockRepository.GenerateMock<IStoreContext>();
            _authenticationService = MockRepository.GenerateMock<IAuthenticationService>();
            _languageService = MockRepository.GenerateMock<ILanguageService>();
            _currencyService = MockRepository.GenerateMock<ICurrencyService>();
            _taxSettings = new TaxSettings();
            _currencySettings = new CurrencySettings();
            _localizationSettings = new LocalizationSettings();
            _userAgentHelper = MockRepository.GenerateMock<IUserAgentHelper>();
            _storeMappingService = MockRepository.GenerateMock<IStoreMappingService>();
            _vendorService = MockRepository.GenerateMock<IVendorService>();
            _productTemplateService = MockRepository.GenerateMock<IProductTemplateService>();
            _dateRangeService = MockRepository.GenerateMock<IDateRangeService>();
            _taxCategoryService = MockRepository.GenerateMock<ITaxCategoryService>();
            _measureService = MockRepository.GenerateMock<IMeasureService>();
            _catalogSettings = new CatalogSettings();

            var nopEngine = MockRepository.GenerateMock<NopEngine>();
            var containe = MockRepository.GenerateMock<IContainer>();
            var containerManager = MockRepository.GenerateMock<ContainerManager>(containe);
            nopEngine.Expect(x => x.ContainerManager).Return(containerManager);

            var urlRecordService = MockRepository.GenerateMock<IUrlRecordService>();

            var genericAttributeService = MockRepository.GenerateMock<IGenericAttributeService>();
            genericAttributeService.Expect(p => p.GetAttributesForEntity(1, "Customer"))
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

            containerManager.Expect(x => x.Resolve<IGenericAttributeService>()).Return(genericAttributeService);
            containerManager.Expect(x => x.Resolve<IUrlRecordService>()).Return(urlRecordService);
            

            EngineContext.Replace(nopEngine);


            var picture = new Picture
            {
                Id = 1,
                SeoFilename = "picture"
            };

            _authenticationService.Expect(p => p.GetAuthenticatedCustomer()).Return(GetTestCustomer());
            _pictureService.Expect(p => p.GetPictureById(1)).Return(picture);
            _pictureService.Expect(p => p.GetThumbLocalPath(picture)).Return(@"c:\temp\picture.png");

            _newsLetterSubscriptionService = MockRepository.GenerateMock<INewsLetterSubscriptionService>();
            _productEditorSettings = new ProductEditorSettings();

            //_genericAttributeService = new GenericAttributeService(_cacheManager, _genericAttributeRepository, _eventPublisher);
            _customerAttributeFormatter = MockRepository.GenerateMock<ICustomerAttributeFormatter>();

            _workContext = new WebWorkContext(_httpContext, _customerService, _vendorService, _storeContext,
                _authenticationService, _languageService, _currencyService, _genericAttributeService, _taxSettings,
                _currencySettings, _localizationSettings, _userAgentHelper, _storeMappingService);

            _exportManager = new ExportManager(_categoryService,
                _manufacturerService, _productAttributeService,
                _pictureService, _newsLetterSubscriptionService,
                _storeService, _workContext, _productEditorSettings,
                _vendorService, _productTemplateService, _dateRangeService,
                _taxCategoryService, _measureService, _catalogSettings,
                _genericAttributeService, _customerAttributeFormatter);
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

                Assert.AreEqual(objectPropertyValue, property.PropertyValue,
                    string.Format("The property \"{0}.{1}\" of these objects is not equal", typeof(T).Name,
                        property.PropertyName));
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

        #endregion

        //[Test]
        //public void Can_export_manufacturers_to_xml()
        //{
        //    var manufacturers = new List<Manufacturer>()
        //    {
        //        new Manufacturer()
        //        {
        //            Id = 1,
        //            Name = "Name",
        //            Description = "Description 1",
        //            MetaKeywords = "Meta keywords",
        //            MetaDescription = "Meta description",
        //            MetaTitle = "Meta title",
        //            PictureId = 0,
        //            PageSize = 4,
        //            PriceRanges = "1-3;",
        //            Published = true,
        //            Deleted = false,
        //            DisplayOrder = 5,
        //            CreatedOnUtc = new DateTime(2010, 01, 01),
        //            UpdatedOnUtc = new DateTime(2010, 01, 02),
        //        },
        //        new Manufacturer()
        //        {
        //            Id = 2,
        //            Name = "Name 2",
        //            Description = "Description 2",
        //            MetaKeywords = "Meta keywords",
        //            MetaDescription = "Meta description",
        //            MetaTitle = "Meta title",
        //            PictureId = 0,
        //            PageSize = 4,
        //            PriceRanges = "1-3;",
        //            Published = true,
        //            Deleted = false,
        //            DisplayOrder = 5,
        //            CreatedOnUtc = new DateTime(2010, 01, 01),
        //            UpdatedOnUtc = new DateTime(2010, 01, 02),
        //        }
        //    };

        //    string result = _exportManager.ExportManufacturersToXml(manufacturers);
        //    //TODO test it
        //    String.IsNullOrEmpty(result).ShouldBeFalse();
        //}

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

            var replacePairce = new Dictionary<string, string>
                {
                    { "OrderId", "Id" },
                    { "OrderStatusId", "OrderStatus" },
                    { "PaymentStatusId", "PaymentStatus" },
                    { "ShippingStatusId", "ShippingStatus" },
                    { "ShippingPickUpInStore", "PickUpInStore" }
                };

            var order = orders.First();

            var ignore = new List<string>();
            ignore.AddRange(replacePairce.Values);

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
                "CustomerTaxDisplayType", "TaxRatesDictionary"
            });

            //fields tested individually
            ignore.AddRange(new[]
            {
               "Customer", "BillingAddress", "ShippingAddress"
            });

            AreAllObjectPropertiesPresent(order, manager, ignore.ToArray());
            PropertiesShouldEqual(order, manager, replacePairce);

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
            replacePairce = addressFilds.ToDictionary(p => billingPatern + p, p => p);
            PropertiesShouldEqual(billingAddress, manager, replacePairce, "CreatedOnUtc", "BillingCountry");
            manager.GetProperties.First(p => p.PropertyName == "BillingCountry").PropertyValue.ShouldEqual(billingAddress.Country.Name);

            const string shippingPatern = "Shipping";
            replacePairce = addressFilds.ToDictionary(p => shippingPatern + p, p => p);
            PropertiesShouldEqual(shippingAddress, manager, replacePairce, "CreatedOnUtc", "ShippingCountry");
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

            var replacePairce = new Dictionary<string, string>();

            var manufacturer = manufacturers.First();

            var ignore = new List<string> { "Picture", "PictureId", "SubjectToAcl", "LimitedToStores", "Deleted", "CreatedOnUtc", "UpdatedOnUtc", "AppliedDiscounts" };

            AreAllObjectPropertiesPresent(manufacturer, manager, ignore.ToArray());
            PropertiesShouldEqual(manufacturer, manager, replacePairce);

            manager.GetProperties.First(p => p.PropertyName == "Picture").PropertyValue.ShouldEqual(@"c:\temp\picture.png");
        }
        #endregion

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
    }
}
