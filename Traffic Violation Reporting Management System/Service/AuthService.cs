using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Traffic_Violation_Reporting_Management_System.Models;
using Traffic_Violation_Reporting_Management_System.DTOs;

namespace Traffic_Violation_Reporting_Management_System.Service
{
    public class AuthService
    {
        private readonly TrafficViolationDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ILogger<AuthService> _logger;

        public AuthService(TrafficViolationDbContext context, IEmailService emailService, ILogger<AuthService> logger)
        {
            _context = context;
            _emailService = emailService;
            _logger = logger;
        }

        public async Task<User?> ValidateUserAsync(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return null;

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower() && u.IsActive == true);

            if (user == null)
                return null;

           
            if (password == user.Password)
                return user;

            return null;
        }

        
        public List<Claim> CreateUserClaims(User user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.MobilePhone, user.PhoneNumber),
                new Claim("CCCD", user.Cccd),
                new Claim(ClaimTypes.Role, GetRoleName(user.Role))
            };

            return claims;
        }

       
        public async Task<bool> EmailExistsAsync(string email)
        {
            return await _context.Users
                .AnyAsync(u => u.Email.ToLower() == email.ToLower());
        }

       
        public async Task<bool> IsUserActiveAsync(string email)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
            
            return user?.IsActive ?? false;
        }

        
        public string HashPassword(string password)
        {
            
            return password;
            
            /* Code hash SHA256 (nếu hash thì bỏ cmt nhé)
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
            */
        }

       
        public bool VerifyPassword(string password, string storedPassword)
        {
            
            return password == storedPassword;
            
            
        }

        /// <summary>
        /// Đăng ký user mới và gửi OTP verification
        /// </summary>
        /// <param name="request">Thông tin đăng ký</param>
        /// <returns>True nếu thành công</returns>
        public async Task<(bool Success, string Message)> RegisterUserAsync(RegisterRequest request)
        {
            try
            {
                _logger.LogInformation("Bắt đầu đăng ký user với email: {Email}", request.Email);
                // Kiểm tra email đã tồn tại chưa
                if (await EmailExistsAsync(request.Email))
                {
                    return (false, "Email đã được đăng ký trong hệ thống");
                }

                // Kiểm tra CCCD đã tồn tại chưa
                if (await CccdExistsAsync(request.Cccd))
                {
                    return (false, "Số CCCD đã được đăng ký trong hệ thống");
                }

                // Kiểm tra số điện thoại đã tồn tại chưa
                if (await PhoneExistsAsync(request.PhoneNumber))
                {
                    return (false, "Số điện thoại đã được đăng ký trong hệ thống");
                }

                // Sử dụng database transaction để đảm bảo tạo user và gửi OTP thành công cùng lúc
                using var transaction = await _context.Database.BeginTransactionAsync();
                
                try
                {
                    // Tạo user mới (chưa active)
                    var newUser = new User
                    {
                        FullName = request.FullName.Trim(),
                        Cccd = request.Cccd.Trim(),
                        PhoneNumber = request.PhoneNumber.Trim(),
                        Email = request.Email.Trim().ToLower(),
                        Address = request.Address?.Trim(),
                        Password = request.Password, // Không hash theo yêu cầu
                        Role = 2, // Citizen role
                        IsActive = false, // Chưa active cho đến khi verify OTP
                        CreatedAt = DateTime.Now
                    };

                    _context.Users.Add(newUser);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("User đã được tạo thành công với ID: {UserId}", newUser.UserId);

                    // Tạo OTP trong database
                    var otpCode = GenerateOtpCode();
                    var otp = new Otp
                    {
                        Email = request.Email, // Sử dụng email column
                        PhoneNumber = "", // Để trống vì không dùng
                        Otpcode = otpCode,
                        CreatedAt = DateTime.Now,
                        ExpiresAt = DateTime.Now.AddMinutes(10), // Hiệu lực 10 phút
                        IsUsed = false
                    };

                    _context.Otps.Add(otp);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("OTP đã được tạo trong database: {OtpCode}", otpCode);

                    // Gửi email OTP
                    _logger.LogInformation("Bắt đầu gửi email OTP tới {Email}", request.Email);
                    var emailSent = await _emailService.SendOtpEmailAsync(request.Email, otpCode, request.FullName);
                    
                    if (!emailSent)
                    {
                        _logger.LogError("❌ Gửi email OTP thất bại cho {Email}", request.Email);
                        throw new Exception("Không thể gửi email xác thực");
                    }

                    // Commit transaction nếu tất cả thành công
                    await transaction.CommitAsync();
                    _logger.LogInformation("✅ Transaction commit thành công - User và OTP đã được tạo, email đã gửi");
                    
                    return (true, "Đăng ký thành công! Vui lòng kiểm tra email để nhận mã xác thực.");
                }
                catch (Exception transactionEx)
                {
                    // Rollback transaction nếu có lỗi
                    await transaction.RollbackAsync();
                    _logger.LogError(transactionEx, "❌ Transaction rollback - Lỗi trong quá trình tạo user hoặc gửi OTP: {Message}", transactionEx.Message);
                    return (false, $"Không thể hoàn thành đăng ký: {transactionEx.Message}. Vui lòng thử lại.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi trong quá trình đăng ký user với email: {Email}", request.Email);
                return (false, "Có lỗi xảy ra trong quá trình đăng ký. Vui lòng thử lại.");
            }
        }



        /// <summary>
        /// Xác thực OTP và kích hoạt tài khoản
        /// </summary>
        /// <param name="request">Thông tin OTP</param>
        /// <returns>True nếu thành công</returns>
        public async Task<(bool Success, string Message)> VerifyOtpAsync(VerifyOtpRequest request)
        {
            try
            {
                // Tìm OTP hợp lệ
                var otp = await _context.Otps
                    .Where(o => o.Email == request.Email.ToLower() 
                               && o.Otpcode == request.OtpCode
                               && !o.IsUsed
                               && o.ExpiresAt > DateTime.Now)
                    .FirstOrDefaultAsync();

                if (otp == null)
                {
                    return (false, "Mã OTP không hợp lệ hoặc đã hết hạn");
                }

                // Đánh dấu OTP đã sử dụng
                otp.IsUsed = true;

                // Kích hoạt tài khoản user
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());

                if (user == null)
                {
                    return (false, "Không tìm thấy tài khoản");
                }

                user.IsActive = true;
                await _context.SaveChangesAsync();

                // Gửi email chào mừng
                await _emailService.SendWelcomeEmailAsync(user.Email, user.FullName);

                return (true, "Xác thực thành công! Tài khoản đã được kích hoạt.");
            }
            catch (Exception ex)
            {
                return (false, "Có lỗi xảy ra trong quá trình xác thực");
            }
        }



        /// <summary>
        /// Gửi OTP để reset password
        /// </summary>
        /// <param name="request">Thông tin forgot password</param>
        /// <returns>True nếu thành công</returns>
        public async Task<(bool Success, string Message)> ForgotPasswordAsync(ForgotPasswordRequest request)
        {
            try
            {
                _logger.LogInformation("Bắt đầu xử lý forgot password cho email: {Email}", request.Email);
                
                // Kiểm tra email có tồn tại và đã active chưa
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower() && u.IsActive == true);

                if (user == null)
                {
                    return (false, "Email không tồn tại hoặc tài khoản chưa được kích hoạt");
                }

                // Xóa các OTP cũ của email này
                var oldOtps = await _context.Otps
                    .Where(o => o.Email == request.Email.ToLower())
                    .ToListAsync();
                
                if (oldOtps.Any())
                {
                    _logger.LogInformation("Xóa {Count} OTP cũ cho {Email}", oldOtps.Count, request.Email);
                    _context.Otps.RemoveRange(oldOtps);
                }

                // Tạo OTP mới
                var otpCode = GenerateOtpCode();
                _logger.LogInformation("Đã tạo OTP code cho forgot password: {OtpCode} cho {Email}", otpCode, request.Email);
                
                var otp = new Otp
                {
                    Email = request.Email.ToLower(),
                    PhoneNumber = "", // Để trống vì không dùng
                    Otpcode = otpCode,
                    CreatedAt = DateTime.Now,
                    ExpiresAt = DateTime.Now.AddMinutes(10), // Hiệu lực 10 phút
                    IsUsed = false
                };

                _context.Otps.Add(otp);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Đã lưu OTP forgot password vào database cho {Email}", request.Email);

                // Gửi email với OTP
                var emailSent = await _emailService.SendForgotPasswordOtpEmailAsync(request.Email, otpCode, user.FullName);
                
                if (!emailSent)
                {
                    _logger.LogError("❌ Gửi email OTP forgot password thất bại cho {Email}", request.Email);
                    return (false, "Không thể gửi email xác thực");
                }

                _logger.LogInformation("✅ Gửi email OTP forgot password thành công cho {Email}", request.Email);
                return (true, "Mã xác thực đã được gửi qua email. Vui lòng kiểm tra hộp thư.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception khi xử lý forgot password cho {Email}: {Message}", request.Email, ex.Message);
                return (false, "Có lỗi xảy ra khi gửi mã xác thực. Vui lòng thử lại.");
            }
        }

        /// <summary>
        /// Reset password với OTP
        /// </summary>
        /// <param name="request">Thông tin reset password</param>
        /// <returns>True nếu thành công</returns>
        public async Task<(bool Success, string Message)> ResetPasswordAsync(ResetPasswordRequest request)
        {
            try
            {
                _logger.LogInformation("Bắt đầu reset password cho email: {Email}", request.Email);
                
                // Tìm OTP hợp lệ
                var otp = await _context.Otps
                    .Where(o => o.Email == request.Email.ToLower() 
                               && o.Otpcode == request.OtpCode
                               && !o.IsUsed
                               && o.ExpiresAt > DateTime.Now)
                    .FirstOrDefaultAsync();

                if (otp == null)
                {
                    return (false, "Mã OTP không hợp lệ hoặc đã hết hạn");
                }

                // Tìm user để reset password
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower() && u.IsActive == true);

                if (user == null)
                {
                    return (false, "Không tìm thấy tài khoản");
                }

                // Đánh dấu OTP đã sử dụng
                otp.IsUsed = true;

                // Cập nhật password mới
                user.Password = request.NewPassword; // Không hash theo yêu cầu

                await _context.SaveChangesAsync();
                _logger.LogInformation("✅ Reset password thành công cho {Email}", request.Email);

                return (true, "Đặt lại mật khẩu thành công! Bạn có thể đăng nhập với mật khẩu mới.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Exception khi reset password cho {Email}: {Message}", request.Email, ex.Message);
                return (false, "Có lỗi xảy ra trong quá trình đặt lại mật khẩu. Vui lòng thử lại.");
            }
        }

        /// <summary>
        /// Kiểm tra CCCD có tồn tại không
        /// </summary>
        /// <param name="cccd">Số CCCD</param>
        /// <returns>True nếu tồn tại</returns>
        public async Task<bool> CccdExistsAsync(string cccd)
        {
            return await _context.Users
                .AnyAsync(u => u.Cccd == cccd);
        }

        /// <summary>
        /// Kiểm tra số điện thoại có tồn tại không
        /// </summary>
        /// <param name="phoneNumber">Số điện thoại</param>
        /// <returns>True nếu tồn tại</returns>
        public async Task<bool> PhoneExistsAsync(string phoneNumber)
        {
            return await _context.Users
                .AnyAsync(u => u.PhoneNumber == phoneNumber);
        }

        /// <summary>
        /// Tạo mã OTP 6 số ngẫu nhiên
        /// </summary>
        /// <returns>Mã OTP</returns>
        private string GenerateOtpCode()
        {
            var random = new Random();
            return random.Next(100000, 999999).ToString();
        }

        private string GetRoleName(int roleId)
        {
            return roleId switch
            {
                0 => "Admin",
                1 => "Officer", 
                2 => "Citizen", 
                _ => "Unknown"
            };
        }
    }
}
