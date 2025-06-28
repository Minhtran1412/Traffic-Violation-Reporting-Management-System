using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using Traffic_Violation_Reporting_Management_System.Models;

namespace Traffic_Violation_Reporting_Management_System.Service
{
    public interface IEmailService
    {
        Task<bool> SendOtpEmailAsync(string email, string otpCode, string userName);
        Task<bool> SendWelcomeEmailAsync(string email, string userName);
        Task<bool> SendForgotPasswordOtpEmailAsync(string email, string otpCode, string userName);
    }

    public class EmailService : IEmailService
    {
        private readonly ILogger<EmailService> _logger;
        private readonly EmailSettings _emailSettings;

        public EmailService(ILogger<EmailService> logger, IOptions<EmailSettings> emailSettings)
        {
            _logger = logger;
            _emailSettings = emailSettings.Value;
        }

        /// <summary>
        /// Gửi email chứa mã OTP
        /// </summary>
        /// <param name="email">Email người nhận</param>
        /// <param name="otpCode">Mã OTP</param>
        /// <param name="userName">Tên người dùng</param>
        /// <returns>True nếu gửi thành công</returns>
        public async Task<bool> SendOtpEmailAsync(string email, string otpCode, string userName)
        {
            try
            {
                var subject = "Mã xác thực đăng ký tài khoản";
                var htmlContent = GenerateOtpEmailHtml(otpCode, userName);
                var textContent = GenerateOtpEmailContent(otpCode, userName);

                var result = await SendEmailAsync(email, subject, htmlContent, textContent);
                
                if (!result)
                {
                    _logger.LogWarning("Không thể gửi email OTP tới {Email}", email);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi gửi email OTP tới {Email}", email);
                return false;
            }
        }

        /// <summary>
        /// Gửi email chào mừng sau khi đăng ký thành công
        /// </summary>
        /// <param name="email">Email người nhận</param>
        /// <param name="userName">Tên người dùng</param>
        /// <returns>True nếu gửi thành công</returns>
        public async Task<bool> SendWelcomeEmailAsync(string email, string userName)
        {
            try
            {
                var subject = "Chào mừng bạn đến với hệ thống!";
                var htmlContent = GenerateWelcomeEmailHtml(userName);
                var textContent = GenerateWelcomeEmailContent(userName);

                var result = await SendEmailAsync(email, subject, htmlContent, textContent);
                
                if (!result)
                {
                    _logger.LogWarning("Không thể gửi email welcome tới {Email}", email);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi gửi email welcome tới {Email}", email);
                return false;
            }
        }

        /// <summary>
        /// Gửi email chứa mã OTP để reset password
        /// </summary>
        /// <param name="email">Email người nhận</param>
        /// <param name="otpCode">Mã OTP</param>
        /// <param name="userName">Tên người dùng</param>
        /// <returns>True nếu gửi thành công</returns>
        public async Task<bool> SendForgotPasswordOtpEmailAsync(string email, string otpCode, string userName)
        {
            try
            {
                var subject = "Mã xác thực đặt lại mật khẩu";
                var htmlContent = GenerateForgotPasswordEmailHtml(otpCode, userName);
                var textContent = GenerateForgotPasswordEmailContent(otpCode, userName);

                var result = await SendEmailAsync(email, subject, htmlContent, textContent);
                
                if (!result)
                {
                    _logger.LogWarning("Không thể gửi email forgot password OTP tới {Email}", email);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi gửi email forgot password OTP tới {Email}", email);
                return false;
            }
        }

        /// <summary>
        /// Gửi email qua SMTP
        /// </summary>
        /// <param name="toEmail">Email người nhận</param>
        /// <param name="subject">Tiêu đề email</param>
        /// <param name="htmlContent">Nội dung HTML</param>
        /// <param name="textContent">Nội dung text thuần</param>
        /// <returns>True nếu gửi thành công</returns>
        private async Task<bool> SendEmailAsync(string toEmail, string subject, string htmlContent, string textContent)
        {
            try
            {
                // Validate email settings
                if (string.IsNullOrEmpty(_emailSettings.SmtpHost))
                {
                    _logger.LogError("SMTP Host không được cấu hình");
                    return false;
                }

                using var smtpClient = new SmtpClient(_emailSettings.SmtpHost, _emailSettings.SmtpPort);
                smtpClient.EnableSsl = _emailSettings.EnableSsl;
                smtpClient.UseDefaultCredentials = _emailSettings.UseDefaultCredentials;
                smtpClient.Timeout = 30000; // 30 seconds timeout
                
                if (!_emailSettings.UseDefaultCredentials)
                {
                    if (string.IsNullOrEmpty(_emailSettings.SmtpUsername) || string.IsNullOrEmpty(_emailSettings.SmtpPassword))
                    {
                        _logger.LogError("SMTP Username hoặc Password không được cấu hình");
                        return false;
                    }
                    
                    smtpClient.Credentials = new NetworkCredential(_emailSettings.SmtpUsername, _emailSettings.SmtpPassword);
                }

                using var message = new MailMessage();
                message.From = new MailAddress(_emailSettings.FromEmail, _emailSettings.FromName);
                message.To.Add(toEmail);
                message.Subject = subject;
                message.IsBodyHtml = true;

                // Tạo multipart message với cả HTML và text
                var htmlView = AlternateView.CreateAlternateViewFromString(htmlContent, null, "text/html");
                var textView = AlternateView.CreateAlternateViewFromString(textContent, null, "text/plain");
                
                message.AlternateViews.Add(htmlView);
                message.AlternateViews.Add(textView);
                
                // Sử dụng CancellationToken để timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await smtpClient.SendMailAsync(message, cts.Token);
                
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Timeout khi gửi email tới {Email} sau 30 giây", toEmail);
                return false;
            }
            catch (SmtpException smtpEx)
            {
                _logger.LogError(smtpEx, "SMTP Error khi gửi email tới {Email}: StatusCode={StatusCode}", 
                    toEmail, smtpEx.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi chung khi gửi email tới {Email}", toEmail);
                return false;
            }
        }

        /// <summary>
        /// Tạo nội dung HTML cho email OTP
        /// </summary>
        /// <param name="otpCode">Mã OTP</param>
        /// <param name="userName">Tên người dùng</param>
        /// <returns>HTML content</returns>
        private string GenerateOtpEmailHtml(string otpCode, string userName)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Mã xác thực OTP</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 30px; text-align: center; border-radius: 10px 10px 0 0; }}
        .content {{ background: #f9f9f9; padding: 30px; border-radius: 0 0 10px 10px; }}
        .otp-code {{ background: #fff; border: 2px solid #667eea; padding: 20px; margin: 20px 0; text-align: center; border-radius: 8px; }}
        .otp-number {{ font-size: 32px; font-weight: bold; color: #667eea; letter-spacing: 5px; }}
        .warning {{ background: #fff3cd; border: 1px solid #ffeaa7; padding: 15px; border-radius: 5px; margin: 20px 0; }}
        .footer {{ text-align: center; margin-top: 30px; color: #666; font-size: 14px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🛡️ Xác thực tài khoản</h1>
            <p>Hệ thống Quản lý Vi phạm Giao thông</p>
        </div>
        <div class='content'>
            <h2>Xin chào {userName},</h2>
            <p>Bạn đã đăng ký tài khoản tại <strong>Hệ thống Quản lý Vi phạm Giao thông</strong>.</p>
            <p>Để hoàn tất quá trình đăng ký, vui lòng sử dụng mã xác thực dưới đây:</p>
            
            <div class='otp-code'>
                <p>Mã xác thực của bạn:</p>
                <div class='otp-number'>{otpCode}</div>
            </div>
            
            <div class='warning'>
                <strong>⚠️ Lưu ý quan trọng:</strong>
                <ul>
                    <li>Mã này có hiệu lực trong <strong>10 phút</strong></li>
                    <li>Không chia sẻ mã này với bất kỳ ai</li>
                    <li>Nếu bạn không thực hiện yêu cầu này, hãy bỏ qua email</li>
                </ul>
            </div>
            
            <p>Cảm ơn bạn đã tham gia hệ thống của chúng tôi!</p>
        </div>
        <div class='footer'>
            <p>© 2025 Hệ thống Quản lý Vi phạm Giao thông</p>
            <p>Email này được gửi tự động, vui lòng không phản hồi.</p>
        </div>
    </div>
</body>
</html>";
        }

        /// <summary>
        /// Tạo nội dung HTML cho email welcome
        /// </summary>
        /// <param name="userName">Tên người dùng</param>
        /// <returns>HTML content</returns>
        private string GenerateWelcomeEmailHtml(string userName)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Chào mừng bạn!</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: linear-gradient(135deg, #28a745 0%, #20c997 100%); color: white; padding: 30px; text-align: center; border-radius: 10px 10px 0 0; }}
        .content {{ background: #f9f9f9; padding: 30px; border-radius: 0 0 10px 10px; }}
        .features {{ background: #fff; padding: 20px; margin: 20px 0; border-radius: 8px; border-left: 4px solid #28a745; }}
        .feature-item {{ margin: 10px 0; padding: 10px 0; border-bottom: 1px solid #eee; }}
        .feature-item:last-child {{ border-bottom: none; }}
        .cta {{ text-align: center; margin: 30px 0; }}
        .cta-button {{ background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 15px 30px; text-decoration: none; border-radius: 25px; display: inline-block; }}
        .footer {{ text-align: center; margin-top: 30px; color: #666; font-size: 14px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🎉 Chào mừng bạn!</h1>
            <p>Tài khoản đã được kích hoạt thành công</p>
        </div>
        <div class='content'>
            <h2>Xin chào {userName},</h2>
            <p><strong>Chúc mừng!</strong> Tài khoản của bạn đã được xác thực và kích hoạt thành công.</p>
            <p>Bạn giờ đây có thể sử dụng đầy đủ các tính năng của <strong>Hệ thống Quản lý Vi phạm Giao thông</strong>:</p>
            
            <div class='features'>
                <div class='feature-item'>
                    <strong>📋 Báo cáo vi phạm:</strong> Báo cáo các hành vi vi phạm giao thông
                </div>
                <div class='feature-item'>
                    <strong>💰 Tra cứu phạt nguội:</strong> Xem thông tin các khoản phạt của bạn
                </div>
                <div class='feature-item'>
                    <strong>📝 Nộp khiếu nại:</strong> Khiếu nại về các quyết định xử phạt
                </div>
                <div class='feature-item'>
                    <strong>🔔 Nhận thông báo:</strong> Cập nhật thông tin mới nhất từ hệ thống
                </div>
            </div>
            
            <div class='cta'>
                <a href='#' class='cta-button'>Đăng nhập ngay</a>
            </div>
            
            <p>Nếu bạn có bất kỳ câu hỏi nào, đừng ngần ngại liên hệ với chúng tôi.</p>
            <p>Cảm ơn bạn đã tham gia hệ thống!</p>
        </div>
        <div class='footer'>
            <p>© 2025 Hệ thống Quản lý Vi phạm Giao thông</p>
            <p>Email này được gửi tự động, vui lòng không phản hồi.</p>
        </div>
    </div>
</body>
</html>";
        }

        private string GenerateOtpEmailContent(string otpCode, string userName)
        {
            return $@"
Xin chào {userName},

Bạn đã đăng ký tài khoản tại Hệ thống Quản lý Vi phạm Giao thông.

Mã xác thực của bạn là: {otpCode}

Mã này có hiệu lực trong 10 phút. Vui lòng không chia sẻ mã này với người khác.

Trân trọng,
Hệ thống Quản lý Vi phạm Giao thông
            ";
        }

        private string GenerateWelcomeEmailContent(string userName)
        {
            return $@"
Xin chào {userName},

Chào mừng bạn đã gia nhập Hệ thống Quản lý Vi phạm Giao thông!

Bạn có thể đăng nhập và sử dụng các tính năng sau:
- Báo cáo vi phạm giao thông
- Xem thông tin phạt nguội
- Nộp khiếu nại
- Nhận thông báo từ hệ thống

Trân trọng,
Hệ thống Quản lý Vi phạm Giao thông
            ";
        }

        /// <summary>
        /// Tạo nội dung HTML cho email forgot password
        /// </summary>
        /// <param name="otpCode">Mã OTP</param>
        /// <param name="userName">Tên người dùng</param>
        /// <returns>HTML content</returns>
        private string GenerateForgotPasswordEmailHtml(string otpCode, string userName)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Đặt lại mật khẩu</title>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: linear-gradient(135deg, #dc3545 0%, #fd7e14 100%); color: white; padding: 30px; text-align: center; border-radius: 10px 10px 0 0; }}
        .content {{ background: #f9f9f9; padding: 30px; border-radius: 0 0 10px 10px; }}
        .otp-code {{ background: #fff; border: 2px solid #dc3545; padding: 20px; margin: 20px 0; text-align: center; border-radius: 8px; }}
        .otp-number {{ font-size: 32px; font-weight: bold; color: #dc3545; letter-spacing: 5px; }}
        .warning {{ background: #fff3cd; border: 1px solid #ffeaa7; padding: 15px; border-radius: 5px; margin: 20px 0; }}
        .footer {{ text-align: center; margin-top: 30px; color: #666; font-size: 14px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🔑 Đặt lại mật khẩu</h1>
            <p>Hệ thống Quản lý Vi phạm Giao thông</p>
        </div>
        <div class='content'>
            <h2>Xin chào {userName},</h2>
            <p>Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn tại <strong>Hệ thống Quản lý Vi phạm Giao thông</strong>.</p>
            <p>Để tiếp tục đặt lại mật khẩu, vui lòng sử dụng mã xác thực dưới đây:</p>
            
            <div class='otp-code'>
                <p>Mã xác thực của bạn:</p>
                <div class='otp-number'>{otpCode}</div>
            </div>
            
            <div class='warning'>
                <strong>⚠️ Lưu ý quan trọng:</strong>
                <ul>
                    <li>Mã này có hiệu lực trong <strong>10 phút</strong></li>
                    <li>Không chia sẻ mã này với bất kỳ ai</li>
                    <li>Nếu bạn không yêu cầu đặt lại mật khẩu, hãy bỏ qua email này</li>
                    <li>Liên hệ hỗ trợ nếu bạn nghi ngờ tài khoản bị xâm nhập</li>
                </ul>
            </div>
            
            <p>Sau khi nhập mã xác thực, bạn sẽ có thể tạo mật khẩu mới cho tài khoản.</p>
        </div>
        <div class='footer'>
            <p>© 2025 Hệ thống Quản lý Vi phạm Giao thông</p>
            <p>Email này được gửi tự động, vui lòng không phản hồi.</p>
        </div>
    </div>
</body>
</html>";
        }

        /// <summary>
        /// Tạo nội dung text cho email forgot password
        /// </summary>
        /// <param name="otpCode">Mã OTP</param>
        /// <param name="userName">Tên người dùng</param>
        /// <returns>Text content</returns>
        private string GenerateForgotPasswordEmailContent(string otpCode, string userName)
        {
            return $@"
Xin chào {userName},

Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn tại Hệ thống Quản lý Vi phạm Giao thông.

Mã xác thực của bạn là: {otpCode}

Mã này có hiệu lực trong 10 phút. Vui lòng không chia sẻ mã này với người khác.

Nếu bạn không yêu cầu đặt lại mật khẩu, hãy bỏ qua email này.

Trân trọng,
Hệ thống Quản lý Vi phạm Giao thông
            ";
        }
    }
} 