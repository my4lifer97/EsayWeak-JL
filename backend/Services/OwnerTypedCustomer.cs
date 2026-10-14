using BarberSaas.Api.Models;

namespace BarberSaas.Api.Services;

// The owner's booking/recurring forms take a typed-in name + phone, and customers are one row per
// (business, phone). When that phone already belongs to a customer, the typed name used to
// overwrite theirs, renaming them on every past appointment too (QA run 2). Now a matching name
// (or one that only adds a missing family name) uses the existing customer, and a different name
// is rejected so the owner picks the existing customer instead. A customer's own booking (verified
// phone) still updates their name -- that's the same person correcting it.
public static class OwnerTypedCustomer
{
    // Null when the typed name fits the existing customer (filling in a blank family name);
    // otherwise the error to return, and the customer is left untouched.
    public static string? MatchExisting(Customer existing, string typedName, string? typedFamilyName)
    {
        var family = typedFamilyName?.Trim() ?? "";
        var sameName = string.Equals(existing.Name.Trim(), typedName.Trim(), StringComparison.OrdinalIgnoreCase);
        var familyConflict = family != "" && existing.FamilyName.Trim() != ""
            && !string.Equals(existing.FamilyName.Trim(), family, StringComparison.OrdinalIgnoreCase);
        if (!sameName || familyConflict)
        {
            var fullName = $"{existing.Name} {existing.FamilyName}".Trim();
            return $"This phone number already belongs to {fullName}. Search for them in the customer field instead.";
        }

        if (existing.FamilyName.Trim() == "" && family != "") existing.FamilyName = family;
        return null;
    }
}
