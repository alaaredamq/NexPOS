using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Application.IServices.Auth;
using Web.Models.Auth;
using Swashbuckle.AspNetCore.Annotations;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, IConfiguration configuration, ILogger<AuthController> logger)
    {
        _authService = authService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("login")]
    [SwaggerOperation(Summary = "User login", Description = "Authenticates a user and returns a JWT token")]
    [SwaggerResponse(200, "Login successful", typeof(LoginResultViewModel))]
    [SwaggerResponse(400, "Invalid credentials")]
    [SwaggerResponse(401, "Authentication failed")]
    public async Task<IActionResult> Login([FromBody] LoginViewModel model)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new LoginResultViewModel
                {
                    Success = false,
                    Message = "Invalid input data"
                });
            }

            var result = await _authService.LoginAsync(model.Username, model.Password);

            if (result.IsSuccess && !string.IsNullOrEmpty(result.Token))
            {
                // Set authentication cookie for web interface
                var claims = new[]
                {
                    new Claim(ClaimTypes.Name, result.User?.Username ?? model.Username),
                    new Claim(ClaimTypes.NameIdentifier, result.User?.Id.ToString() ?? "0"),
                    new Claim(ClaimTypes.Email, result.User?.Email ?? ""),
                    new Claim("FullName", result.User?.GetFullName() ?? ""),
                    new Claim("Role", result.User?.Role?.Name ?? ""),
                    new Claim("RoleId", result.User?.RoleId.ToString() ?? "0")
                };

                var claimsIdentity = new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme);
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = model.RememberMe,
                    ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : DateTimeOffset.UtcNow.AddHours(8)
                };

                await HttpContext.SignInAsync(JwtBearerDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity), authProperties);

                return Ok(new LoginResultViewModel
                {
                    Success = true,
                    Token = result.Token,
                    Message = "Login successful",
                    ReturnUrl = model.ReturnUrl
                });
            }

            return Unauthorized(new LoginResultViewModel
            {
                Success = false,
                Message = result.ErrorMessage ?? "Invalid username or password"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login error for user: {Username}", model.Username);
            return StatusCode(500, new LoginResultViewModel
            {
                Success = false,
                Message = "An error occurred during login"
            });
        }
    }

    [HttpPost("logout")]
    [Authorize]
    [SwaggerOperation(Summary = "User logout", Description = "Logs out the current user")]
    [SwaggerResponse(200, "Logout successful")]
    public async Task<IActionResult> Logout()
    {
        try
        {
            await HttpContext.SignOutAsync(JwtBearerDefaults.AuthenticationScheme);
            return Ok(new { Success = true, Message = "Logout successful" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Logout error");
            return StatusCode(500, new { Success = false, Message = "An error occurred during logout" });
        }
    }

    [HttpGet("me")]
    [Authorize]
    [SwaggerOperation(Summary = "Get current user", Description = "Returns information about the currently authenticated user")]
    [SwaggerResponse(200, "User information retrieved successfully")]
    [SwaggerResponse(401, "User not authenticated")]
    public IActionResult GetCurrentUser()
    {
        try
        {
            var userInfo = new
            {
                Id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                Username = User.FindFirst(ClaimTypes.Name)?.Value,
                Email = User.FindFirst(ClaimTypes.Email)?.Value,
                FullName = User.FindFirst("FullName")?.Value,
                Role = User.FindFirst("Role")?.Value,
                RoleId = User.FindFirst("RoleId")?.Value
            };

            return Ok(userInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get current user error");
            return StatusCode(500, new { Success = false, Message = "An error occurred while retrieving user information" });
        }
    }
}