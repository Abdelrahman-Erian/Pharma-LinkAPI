﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Pharma_LinkAPI.DTO.AccountDTO;
using Pharma_LinkAPI.Identity;
using Pharma_LinkAPI.Repositries.Irepositry;
using Pharma_LinkAPI.Services.JWT;

namespace Pharma_LinkAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly IJwtService _jwtService;
        private readonly IUnitOfWork _unitOfWork;

        public AccountController(IUnitOfWork unitOfWork, UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, IJwtService jwtService)
        {
            _unitOfWork = unitOfWork;
            _userManager = userManager;
            _signInManager = signInManager;
            _jwtService = jwtService;

        }
        [Authorize(Roles = SD.Role_Admin)]
        [HttpPost("Register/{Id}")]
        public async Task<ActionResult<AuthentcationResponse>> Register(int Id)
        {
            var request = await _unitOfWork._requestRepositry.GetById(Id);
            if (request == null)
            {
                return NotFound("Request not found.");
            }
            if (!ModelState.IsValid)
            {
                string errors = string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return BadRequest(errors);
            }
            var user = new AppUser
            {
                UserName = request.UserName,
                Email = request.Email,
                PhoneNumber = request.Phone,
                LiscnceNumber = request.Pharmacy_License,
                Street = request.Street,
                State = request.State,
                City = request.City,
                Name = request.Pharmacy_Name,
                Role = SD.Role_Pharmacy,
                EmailConfirmed = true,
                DrName = request.DR_Name
            };
            string? password = request.Password;
            if (password == null || password[0] == ' ')
            {
                return BadRequest("Password is required.");
            }
            var result = await _userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(user, SD.Role_Pharmacy);
                //add cart to user

                var cart = new Cart
                {
                    TotalPrice = 0,
                    PharmacyId = user.Id
                };
                await _unitOfWork._cartRepositry.AddCart(cart);
                await _unitOfWork._requestRepositry.Delete(Id);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _unitOfWork._emailService.SendEmailAsync(request.Email, "Your Pharmacy Account Has Been Created", $"Your request has been Accepted successfully.\n username is {request.UserName}\n\npassword is {request.Password}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to send email: {ex.Message}");
                    }
                }); return Ok("User added succeffuly");

            }
            string error = string.Join(" | ", result.Errors.Select(x => x.Description));
            return BadRequest(error);
        }
        [Authorize(Roles = SD.Role_Admin)]
        [HttpPost("CompanyRegister")]
        public async Task<ActionResult<string>> Register(CompanyRegisterDTO companyRegisterDTO)
        {
            if (!ModelState.IsValid)
            {
                string errors = string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return BadRequest(errors);
            }
            var existEmail = await _userManager.FindByEmailAsync(companyRegisterDTO.Email);
            if (existEmail != null)
            {
                return BadRequest("Email is already in use");
            }

            var user = new AppUser
            {
                UserName = companyRegisterDTO.UserName,
                Email = companyRegisterDTO.Email,
                PhoneNumber = companyRegisterDTO.PhoneNumber,
                LiscnceNumber = companyRegisterDTO.LicenseNumber,
                Street = companyRegisterDTO.Street,
                State = companyRegisterDTO.State,
                City = companyRegisterDTO.City,
                Name = companyRegisterDTO.Name,
                Role = SD.Role_Company,
                MinPriceToMakeOrder = companyRegisterDTO.MinPriceToMakeOrder,
                EmailConfirmed = true
            };
            string? password = companyRegisterDTO.Password;
            if (password == null || password[0] == ' ')
            {
                return BadRequest("Password is required.");
            }
            var result = await _userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(user, SD.Role_Company);
                await _unitOfWork._emailService.SendEmailAsync(user.Email, "Your Company Account Has Been Created", $"Your Account has been Added successfully.\n username is {user.UserName}\n\npassword is {password}");
                return Ok("company is created");
            }
            string error = string.Join(" | ", result.Errors.Select(x => x.Description));
            return BadRequest(error);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDTO loginDTO)
        {
            // Check if the model state is valid
            if (!ModelState.IsValid)
            {
                string errors = string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return BadRequest(errors);
            }

            var email = await _userManager.FindByEmailAsync(loginDTO.UserName);
            if (email != null)
            {
                loginDTO.UserName = email.UserName;
            }

            // Check if the user exists
            var result = await _signInManager.PasswordSignInAsync(loginDTO.UserName, loginDTO.Password, isPersistent: loginDTO.RememberMe, lockoutOnFailure: false);
            if (result.Succeeded)
            {
                var user = await _userManager.FindByNameAsync(loginDTO.UserName);
                if (user == null)
                {
                    return BadRequest("User not found.");
                }
                if (user.IsDeleted)
                {
                    return BadRequest("User is banned.");
                }
                await _signInManager.SignInAsync(user, isPersistent: false);

                // Generate JWT token 

                var token = _jwtService.CreateToken(user);

                return Ok(token);
            }
            return BadRequest("Invalid User Name or Password");
        }

        [HttpGet("logout")]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return NoContent();
        }


        [Authorize(Roles = SD.Role_Admin)]
        [HttpDelete("DeleteUser")]
        public async Task<IActionResult> DeleteUser(string userName)
        {

            if (userName == "admin")
            {
                return BadRequest("You can't delete the admin account.");
            }
            if (string.IsNullOrEmpty(userName))
            {
                return BadRequest("User name is required.");
            }
            var user = await _userManager.FindByNameAsync(userName);
            if (user == null)
            {
                return NotFound("User not found.");
            }
            if (user.pdfPath != null)
            {
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), user.pdfPath);
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }
            if (user.ImagePath != null)
            {
                var imgPath = Path.Combine(Directory.GetCurrentDirectory(), user.ImagePath);
                if (System.IO.File.Exists(imgPath))
                {
                    System.IO.File.Delete(imgPath);
                }
            }

            if (user.Role == SD.Role_Pharmacy)
            {
                var reviews = await _unitOfWork._reviewRepositiry.GetReviewsByPharmacyId(user.Id);

                if (reviews != null)
                {
                    foreach (var review in reviews)
                    {
                        await _unitOfWork._reviewRepositiry.Delete(review.Id);
                    }
                }
            }
            if (user.Role == SD.Role_Company)
            {
                var reviews = await _unitOfWork._accountRepositry.GetUserById(user.Id);
                if (reviews.ReviewsReceived != null)
                {
                    foreach (var review in reviews.ReviewsReceived)
                    {
                        await _unitOfWork._reviewRepositiry.Delete(review.Id);
                    }
                }

            }
            user.IsDeleted = true;
            await _unitOfWork.SaveAsync();
            return Ok("User deleted successfully.");

        }

    }
}
