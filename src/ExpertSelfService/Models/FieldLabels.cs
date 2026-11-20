using ExpertSelfService.Services;

namespace ExpertSelfService.Models;

/// <summary>
/// The name of a changed field as the expert's own pages call it. Used for pending changes (S-B, S-C) and the change
/// log (S-C). The keys are the column names the services write to ss_pending_changes.field and ss_change_log.field.
/// A field not listed here shows its stored name, so a new field is never hidden.
/// </summary>
public static class FieldLabels
{
    public static string For(TextService text, string field) => field switch
    {
        NameRules.FirstName => text["profile.first_name"],
        NameRules.MiddleName => text["profile.middle_name"],
        NameRules.LastName => text["profile.last_name"],
        "Street" => text["profile.street"],
        "ZipcodeId" => text["profile.postcode_city"],
        "PhoneMobile" => text["profile.phone_mobile"],
        "PhoneWork" => text["profile.phone_work"],
        "PhoneHome" => text["profile.phone_home"],
        "Email" => text["profile.email"],
        "Company" => text["profile.company"],
        "Cvr" => text["profile.cvr"],
        "Designation" => text["profile.designation"],
        SkillRules.Field => text["profile.section.skills"],
        _ => field,
    };
}
