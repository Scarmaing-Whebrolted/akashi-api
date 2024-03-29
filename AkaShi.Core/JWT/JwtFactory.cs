using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
using AkaShi.Core.Auth;
using AkaShi.Core.Exceptions;
using AkaShi.Core.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AkaShi.Core.JWT;

public sealed class JwtFactory
{
    private readonly JwtIssuerOptions _jwtOptions;
    private readonly JwtSecurityTokenHandler _jwtSecurityTokenHandler;

    public JwtFactory(IOptions<JwtIssuerOptions> jwtOptions)
    {
        _jwtOptions = jwtOptions.Value;
        ThrowIfInvalidOptions(_jwtOptions);

        _jwtSecurityTokenHandler = new JwtSecurityTokenHandler();
    }

    public async Task<AccessToken> GenerateAccessToken(int id, string userName, string email)
    {
        var identity = GenerateClaimsIdentity(id, userName);

        var claims = new[]
        {
             new Claim(JwtRegisteredClaimNames.Sub, userName),
             new Claim(JwtRegisteredClaimNames.Email, email),
             new Claim(JwtRegisteredClaimNames.Jti, await _jwtOptions.JtiGenerator()),
             new Claim(JwtRegisteredClaimNames.Iat, ToUnixEpochDate(_jwtOptions.IssuedAt).ToString(), 
                 ClaimValueTypes.Integer64),
             identity.FindFirst("id")
         };

        // Create the JWT security token and encode it.
        var jwt = new JwtSecurityToken(
            _jwtOptions.Issuer,
            _jwtOptions.Audience,
            claims,
            _jwtOptions.NotBefore,
            _jwtOptions.Expiration,
            _jwtOptions.SigningCredentials);

        return new AccessToken(_jwtSecurityTokenHandler.WriteToken(jwt), (int)_jwtOptions.ValidFor.TotalSeconds);
    }

    public string GenerateRefreshToken()
    {
        return Convert.ToBase64String(SecurityHelper.GetRandomBytes());
    }

    public ClaimsPrincipal GetPrincipalFromToken(string token, string signingKey)
    {
        return ValidateToken(token, new TokenValidationParameters
        {
            ValidateAudience = false,
            ValidateIssuer = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = false // we check expired tokens here
        });
    }

    public int GetUserIdFromToken(string accessToken, string signingKey)
    {
        var claimsPrincipal = GetPrincipalFromToken(accessToken, signingKey);

        // invalid token/signing key was passed and we can't extract user claims
        if (claimsPrincipal == null)
        {
            throw new InvalidTokenException("access");
        }

        return int.Parse(claimsPrincipal.Claims.First(c => c.Type == "id").Value);
    }

    private ClaimsPrincipal ValidateToken(string token, TokenValidationParameters tokenValidationParameters)
    {
        try
        {
            var principal = _jwtSecurityTokenHandler
                .ValidateToken(token, tokenValidationParameters, out var securityToken);

            if (!(securityToken is JwtSecurityToken jwtSecurityToken) 
                || !jwtSecurityToken.Header.Alg
                    .Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            {
                throw new SecurityTokenException("Invalid token");
            }

            return principal;
        }
        catch (Exception)
        {
            // Token validation failed
            return null;
        }
    }

    private static ClaimsIdentity GenerateClaimsIdentity(int id, string userName)
    {
        return new ClaimsIdentity(new GenericIdentity(userName, "Token"), new[]
        {
            new Claim("id", id.ToString())
        });
    }

    /// <returns>Date converted to seconds since Unix epoch (Jan 1, 1970, midnight UTC).</returns>
    private static long ToUnixEpochDate(DateTime date)
    {
        return (long)Math
            .Round((date.ToUniversalTime() 
                    - new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero))
                .TotalSeconds);
    }

    private static void ThrowIfInvalidOptions(JwtIssuerOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (options.ValidFor <= TimeSpan.Zero)
        {
            throw new ArgumentException("Must be a non-zero TimeSpan.", nameof(JwtIssuerOptions.ValidFor));
        }

        if (options.SigningCredentials == null)
        {
            throw new ArgumentNullException(nameof(JwtIssuerOptions.SigningCredentials));
        }

        if (options.JtiGenerator == null)
        {
            throw new ArgumentNullException(nameof(JwtIssuerOptions.JtiGenerator));
        }
    }
}