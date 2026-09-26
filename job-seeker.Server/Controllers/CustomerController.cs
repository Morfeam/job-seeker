using job_seeker.Server.Authorization;
using job_seeker.Server.Models;
using job_seeker.Server.Operations.Auth;
using job_seeker.Server.Operations.Email;
using job_seeker.Server.Operations.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace job_seeker.Server.Controllers;

[ApiController]
[Route("customer")]
public class CustomerController : ControllerBase
{
    private readonly ICustomerStorage _customerStorage;
    private readonly ISendEmail _sendEmail;
    private readonly TokenHandler _tokenHandler;
    private readonly IConfiguration _config;

    private static readonly Regex PasswordRegex =
        new(@"^(?=.*[A-Z])(?=.*[^a-zA-Z0-9]).{8,29}$");

    public CustomerController(
        ICustomerStorage customerStorage,
        ISendEmail sendEmail,
        TokenHandler tokenHandler,
        IConfiguration config)
    {
        _customerStorage = customerStorage;
        _sendEmail = sendEmail;
        _tokenHandler = tokenHandler;
        _config = config;
    }

    [HttpPost("sign-up")]
    public async Task<IActionResult> SignUp([FromBody] SignUpRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (!PasswordRegex.IsMatch(request.Password))
            return BadRequest(new { error = "Password must be 8–29 characters and contain at least one uppercase letter and one special character." });

        if (!request.AcceptedTerms)
            return BadRequest(new { error = "You must accept the Terms of Service to create an account." });

        if (await _customerStorage.EmailExistsAsync(request.Email))
            return Conflict(new { error = "An account with that email already exists." });

        var salt = PasswordHasher.GenerateSalt();
        var hash = PasswordHasher.GenerateHash(request.Password, salt);
        var id = Guid.NewGuid().ToString();

        var customer = new SecureCustomer
        {
            Id = id,
            Email = request.Email,
            PasswordHash = hash,
            PasswordSalt = salt,
            FirstName = request.FirstName,
            LastName = request.LastName,
            CreatedDate = DateTime.UtcNow.ToString("o"),
            TermsVersion = request.TermsVersion,
            TermsAcceptedDate = DateTimeOffset.UtcNow,
        };

        await _customerStorage.CreateAsync(customer);

        return Ok(new AuthResponse
        {
            Token = _tokenHandler.CreateToken(id),
            SessionStartDate = DateTime.UtcNow,
        });
    }

    [HttpPost("sign-in")]
    public async Task<IActionResult> SignIn([FromBody] SignInRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var customer = await _customerStorage.GetByEmailAsync(request.Email);
        if (customer == null || !PasswordHasher.Verify(request.Password, customer.PasswordSalt, customer.PasswordHash))
            return Unauthorized(new { error = "Incorrect email or password." });

        return Ok(new AuthResponse
        {
            Token = _tokenHandler.CreateToken(customer.Id),
            SessionStartDate = DateTime.UtcNow,
        });
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var customer = await _customerStorage.GetByEmailAsync(request.Email);
        if (customer != null)
        {
            var resetToken = _tokenHandler.CreatePasswordResetToken(customer.Id);
            var appBaseUrl = _config["app-base-url"]?.TrimEnd('/') ?? string.Empty;
            var resetLink = $"{appBaseUrl}/reset-password/{resetToken}";
            await _sendEmail.SendPasswordResetAsync(customer.Email, resetLink);
        }

        // Always 200 — don't reveal whether the email exists
        return Ok();
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (!PasswordRegex.IsMatch(request.Password))
            return BadRequest(new { error = "Password must be 8–29 characters and contain at least one uppercase letter and one special character." });

        var customerId = await _tokenHandler.ValidatePasswordResetTokenAsync(request.Token);
        if (customerId == null)
            return BadRequest(new { error = "Reset link is invalid or has expired. Please request a new one." });

        var customer = await _customerStorage.GetByIdAsync(customerId);
        if (customer == null)
            return BadRequest(new { error = "Account not found." });

        var newSalt = PasswordHasher.GenerateSalt();
        var newHash = PasswordHasher.GenerateHash(request.Password, newSalt);
        await _customerStorage.UpdatePasswordAsync(customer.Id, customer.Email, newHash, newSalt);

        return Ok();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var customerId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(customerId))
            return Unauthorized();

        var customer = await _customerStorage.GetByIdAsync(customerId);
        if (customer == null)
            return NotFound();

        return Ok(new Customer
        {
            Id = customer.Id,
            Email = customer.Email,
            FirstName = customer.FirstName,
            LastName = customer.LastName,
        });
    }
}
