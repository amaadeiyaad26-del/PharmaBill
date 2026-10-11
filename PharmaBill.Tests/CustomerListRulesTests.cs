using PharmaBill.App.ViewModels;
using Xunit;

namespace PharmaBill.Tests;

public class CustomerListRulesTests
{
    [Theory]
    [InlineData("Retailer", true)]
    [InlineData("Hospital", true)]
    [InlineData(" institution ", true)]
    [InlineData("Distributor", false)]
    [InlineData("Other", false)]
    [InlineData("", false)]
    public void Retail_buyer_types_are_recognised(string buyerType, bool expected)
    {
        Assert.Equal(expected, CustomerListRules.IsRetailBuyerType(buyerType));
    }

    [Fact]
    public void Each_non_patient_customer_lands_in_exactly_one_buyer_list()
    {
        Assert.True(CustomerListRules.Belongs(CustomerListView.RetailBuyers, "Retailer", isPatient: false));
        Assert.False(CustomerListRules.Belongs(CustomerListView.WholesaleBuyers, "Retailer", isPatient: false));
        Assert.True(CustomerListRules.Belongs(CustomerListView.WholesaleBuyers, "Distributor", isPatient: false));
        Assert.False(CustomerListRules.Belongs(CustomerListView.RetailBuyers, "Distributor", isPatient: false));
    }

    [Fact]
    public void Patients_never_appear_in_either_buyer_list()
    {
        Assert.True(CustomerListRules.Belongs(CustomerListView.Patients, "Walk-in", isPatient: true));
        Assert.False(CustomerListRules.Belongs(CustomerListView.WholesaleBuyers, "Walk-in", isPatient: true));
        Assert.False(CustomerListRules.Belongs(CustomerListView.RetailBuyers, "Retailer", isPatient: true));
    }

    [Theory]
    [InlineData("Walk-in", true)]
    [InlineData("Chronic Patient", true)]
    [InlineData("Retail", true)]
    [InlineData("Retailer", false)]
    [InlineData("Distributor", false)]
    public void Patient_types_are_separate_from_buyer_types(string buyerType, bool expected)
    {
        Assert.Equal(expected, CustomerListRules.IsPatientType(buyerType));
    }
}
