namespace Core.Auditing;

/// <summary>
/// Keeps a property out of the audit trail entirely (not even its name is recorded as changed).
/// For derived/cached values that change constantly and tell a support engineer nothing.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditIgnoreAttribute : Attribute
{
}

/// <summary>
/// The property's changes are recorded, but its values are replaced with "[REDACTED]". Properties
/// whose names look like secrets (hash, token, password, salt, ...) are redacted automatically; use
/// this for anything the name doesn't give away.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditSensitiveAttribute : Attribute
{
}
