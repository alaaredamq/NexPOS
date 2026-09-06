using Application.IServices.Auth;
using Core.Entities.Users;
using Core.IRepositories.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Application.Services.Auth;

public class JwtService : IJwtService
{
    private readonly IConfiguration _configuration;
    private readonly IUserRepository _userRepository;
    private readonly string _secretKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expirationMinutes;

    public JwtService(IConfiguration configuration, IUserRepository userRepository)
    {
        _configuration = configuration;
        _userRepository = userRepository;

        // No fallback here on purpose: the old fallback ("your-super-secret-key...") was also
        // committed to appsettings.json, so anyone who ever saw this repo knows it. Silently
        // signing tokens with a publicly-known key is worse than refusing to start.
        _secretKey = _configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException(
                "Jwt:SecretKey is not configured. Set it via 'dotnet user-secrets set \"Jwt:SecretKey\" \"<value>\"' " +
                "in development, or the Jwt__SecretKey environment variable in production. It must be at least 32 characters.");
        if (_secretKey.Length < 32)
            throw new InvalidOperationException("Jwt:SecretKey must be at least 32 characters long.");

        _issuer = _configuration["Jwt:Issuer"] ?? "NexPos";
        _audience = _configuration["Jwt:Audience"] ?? "NexPos-Users";
        _expirationMinutes = int.Parse(_configuration["Jwt:ExpirationMinutes"] ?? "60");
    }

    public string GenerateAccessToken(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_secretKey);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),
            new("role", user.Role.Name),
            new("roleId", user.RoleId.ToString())
        };

        // Add permissions as claims
        var rolePermissions = user.Role.RolePermissions.Where(rp => rp.IsGranted).Select(rp => rp.Permission.Name);
        var userPermissions = user.UserPermissions.Where(up => up.IsGranted && !up.IsExpired()).Select(up => up.Permission.Name);
        var allPermissions = rolePermissions.Union(userPermissions).Distinct();
        
        foreach (var permission in allPermissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_expirationMinutes),
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        return Guid.NewGuid().ToString();
    }

    public string GenerateSessionToken()
    {
        return Guid.NewGuid().ToString();
    }

    public bool ValidateToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_secretKey);

            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out SecurityToken validatedToken);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<User?> GetUserFromTokenAsync(string token)
    {
        if (!ValidateToken(token))
            return null;

        var tokenHandler = new JwtSecurityTokenHandler();
        var jwtToken = tokenHandler.ReadJwtToken(token);
        var userIdClaim = jwtToken.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier);

        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            return null;

        return await _userRepository.GetByIdAsync(userId);
    }
}
