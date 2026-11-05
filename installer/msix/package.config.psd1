# MSIX package identity for JsonNotepad, read by build/package-msix.ps1 (Import-PowerShellDataFile).
#
# IMPORTANT - Microsoft Store:
#   These three values MUST match exactly what Partner Center shows for the reserved app name under
#   Product management > Product identity:
#     IdentityName          <- "Package/Identity/Name"            (e.g. 12345ContosoLtd.JsonNotepad)
#     Publisher             <- "Package/Identity/Publisher"       (e.g. CN=1A2B3C4D-5E6F-7A8B-9C0D-112233445566)
#     PublisherDisplayName  <- "Package/Properties/PublisherDisplayName" (your Partner Center publisher name)
#   A mismatch makes the Store reject the upload.
#
# Sideloading / local testing:
#   The placeholder values below work as-is. The Publisher string is also the Subject of the
#   self-signed certificate created by `build/package-msix.ps1 -CreateDevCertificate`, and of any
#   .pfx you pass with -Sign: the signing certificate Subject must equal Publisher character for character.
#
# Values can be overridden per run without editing this file:
#   build/package-msix.ps1 -IdentityName ... -Publisher ... -PublisherDisplayName ...
@{
    # Letters, digits, '.' and '-' only; 3-50 characters.
    IdentityName         = 'JsonNotepadContributors.JsonNotepad'

    # An X.500 distinguished name, e.g. 'CN=Contoso Ltd' or the CN=GUID string from Partner Center.
    Publisher            = 'CN=JsonNotepad Contributors'

    # Human-readable publisher name shown in Settings > Apps and in the Store.
    PublisherDisplayName = 'JsonNotepad Contributors'
}
