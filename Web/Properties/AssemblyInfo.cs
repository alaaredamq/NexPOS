using Microsoft.Extensions.Configuration.UserSecrets;

// This project sets <GenerateAssemblyInfo>false</GenerateAssemblyInfo>, which also suppresses the
// [assembly: UserSecretsId] attribute the SDK would normally emit from <UserSecretsId> in the
// .csproj. Without that attribute WebApplication.CreateBuilder cannot locate the user-secrets
// store in Development, so Jwt:SecretKey (set via `dotnet user-secrets`, see S7) never loads and
// startup fails. Declaring it here by hand restores that lookup. Keep the id in sync with
// <UserSecretsId> in Web.csproj.
[assembly: UserSecretsId("707edb39-2b26-4e6c-ad8b-bf4c47c1dc7b")]
