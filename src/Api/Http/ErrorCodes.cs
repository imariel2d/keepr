namespace Keepr.Api.Http;

/// <summary>
/// The canonical set of stable, machine-readable <c>code</c>s the API attaches to user-facing
/// problem+json errors (#30). The client owns the translated copy for each code and renders it in
/// the user's language, falling back to the English <c>detail</c> for any code it doesn't map — so
/// <b>these string values are contract</b>: reword a <c>detail</c> freely, but never rename a code
/// (add a new one instead). Kept in one place so codes are discoverable and can't silently diverge
/// from the client's <c>ERROR_MESSAGES</c> map (the i18n-translations skill polices the pair).
/// See docs/feature-30-localization.md §5.
/// </summary>
public static class ErrorCodes
{
    /// <summary>Generic fallback for a caller-fixable request with no more specific code. Also the
    /// default carried by <c>FolderException</c>/<c>TrashException</c> when a throw omits one.</summary>
    public const string InvalidRequest = "invalid_request";

    // ---- Auth / credentials -------------------------------------------------
    public const string InvalidCredentials = "invalid_credentials";
    public const string EmailRegistered = "email_registered";
    public const string RegistrationClosed = "registration_closed";
    public const string PasswordIncorrect = "password_incorrect";

    // ---- Profile / localization --------------------------------------------
    public const string InvalidLanguage = "invalid_language";

    // ---- Email change (#27) — client branches on these; do not rename -------
    public const string EmailInUse = "email_in_use";
    public const string EmailUnchanged = "email_unchanged";
    public const string EmailChangePending = "email_change_pending";
    public const string ConfirmLinkInvalid = "confirm_link_invalid";

    // ---- Email delivery / provider (client branches on the first two) -------
    public const string EmailNotConfigured = "email_not_configured";
    public const string EmailUnverified = "email_unverified";
    public const string EmailTestInProgress = "email_test_in_progress";

    // ---- Recovery / invite links -------------------------------------------
    public const string ResetLinkInvalid = "reset_link_invalid";
    public const string InviteLinkInvalid = "invite_link_invalid";

    // ---- Admin account management ------------------------------------------
    public const string QuotaInvalid = "quota_invalid";
    public const string RoleInvalid = "role_invalid";
    public const string CannotRemoveSelf = "cannot_remove_self";
    public const string CannotDemoteSelf = "cannot_demote_self";
    public const string LastAdmin = "last_admin";
    public const string AccountAlreadyClaimed = "account_already_claimed";
    public const string AccountNotClaimed = "account_not_claimed";
    public const string InviteConflict = "invite_conflict";
    public const string InviteSendFailed = "invite_send_failed";
    public const string ResetConflict = "reset_conflict";
    public const string ResetSendFailed = "reset_send_failed";

    // ---- Search -------------------------------------------------------------
    public const string SearchTermRequired = "search_term_required";

    // ---- Sharing / preview --------------------------------------------------
    public const string ShareExpiryTooShort = "share_expiry_too_short";
    public const string ShareRevoked = "share_revoked";
    public const string ShareNotFound = "share_not_found";
    public const string ShareUnavailable = "share_unavailable";
    public const string PreviewUnsupported = "preview_unsupported";

    // ---- Folders / files / names -------------------------------------------
    public const string FolderNotFound = "folder_not_found";
    public const string FileNotFound = "file_not_found";
    public const string FolderDepthExceeded = "folder_depth_exceeded";
    public const string FolderMoveIntoSelf = "folder_move_into_self";
    public const string FolderMoveIntoDescendant = "folder_move_into_descendant";
    public const string NameRequired = "name_required";
    public const string NameTooLong = "name_too_long";
    public const string NameInvalid = "name_invalid";

    // ---- Trash --------------------------------------------------------------
    public const string TrashItemNotFound = "trash_item_not_found";
    public const string RestoreParentFirst = "restore_parent_first";

    // ---- Quota --------------------------------------------------------------
    public const string QuotaExceeded = "quota_exceeded";
}
