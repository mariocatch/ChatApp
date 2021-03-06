using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using ChatApp.Server.Models;
using ChatApp.Server.Models.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ChatApp.Server.Controllers
{
    public class AccountController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly JwtSecurityTokenHandler _jwtTokenHandler = new JwtSecurityTokenHandler();
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IConfiguration configuration)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _configuration = configuration;
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Login()
        {
            var (headerUserName, headerPassword) = GetAuthLoginInformation(HttpContext);

            var signInResult = await _signInManager.PasswordSignInAsync(headerUserName, headerPassword, false, false);
            if (!signInResult.Succeeded)
            {
                return Unauthorized();
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, headerUserName), new Claim(ClaimTypes.Name, headerUserName) };
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(claims: claims, signingCredentials: credentials);
            return new OkObjectResult(_jwtTokenHandler.WriteToken(token));
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Register()
        {
            var (headerUserName, headerPassword) = GetAuthLoginInformation(HttpContext);

            var userResult = await _userManager.FindByNameAsync(headerUserName);
            if (userResult != null)
            {
                // if they somehow get here, they're already logged in and are confirmed to be the requested user for registration,
                //  so just return 200 and let them continue on
                if (HttpContext.User.Identity.Name == userResult.UserName && HttpContext.User.Identity.IsAuthenticated)
                {
                    return new OkResult();
                }

                // user already exists, but someone else (who isn't logged in as that user) is trying to
                //  create an account with the same name
                return new UnauthorizedResult();
            }

            var user = new ApplicationUser
            {
                UserName = headerUserName
            };

            var createResult = await _userManager.CreateAsync(user, headerPassword);
            if (!createResult.Succeeded)
            {
                // keep a 401 here to remain consistent with the rest of the possible status codes
                //  sent back from this endpoint - that way attackers won't know the difference
                //  between an account existing already or not
                return new UnauthorizedResult();
            }

            // sign the user in after creation
            await _signInManager.SignInAsync(user, false);
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));
            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, headerUserName), new Claim(ClaimTypes.Name, headerUserName) };
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(claims: claims, signingCredentials: credentials);
            return new OkObjectResult(_jwtTokenHandler.WriteToken(token));
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();

            return new OkResult();
        }

        [HttpGet]
        [Authorize]
        public IEnumerable<UserDTO> Users() => _userManager.Users.Select(ApplicationUser.Projection);

        private static (string, string) GetAuthLoginInformation(HttpContext context)
        {
            var authHeader = context.Request.Headers["Authorization"].First().Substring("Basic ".Length).Trim();
            var encoding = Encoding.GetEncoding("iso-8859-1");
            var split = encoding.GetString(Convert.FromBase64String(authHeader)).Split(':');
            return (split[0], split[1]);
        }
    }
}