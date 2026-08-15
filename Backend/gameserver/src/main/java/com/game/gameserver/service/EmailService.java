package com.game.gameserver.service;

import org.springframework.beans.factory.annotation.Value;
import org.springframework.mail.javamail.JavaMailSender;
import org.springframework.mail.javamail.MimeMessageHelper;
import org.springframework.stereotype.Service;

import jakarta.mail.internet.MimeMessage;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;

@Service
@RequiredArgsConstructor
@Slf4j
public class EmailService {

    private final JavaMailSender mailSender;

    @Value("${app.base-url}")
    private String baseUrl;

    @Value("${spring.mail.username}")
    private String senderEmail;

    public void sendVerificationEmail(String toEmail, String token) {
        log.info("인증 메일 발송 시작: email={}, token={}", toEmail, token);

        MimeMessage mimeMessage = mailSender.createMimeMessage();

        try {
            MimeMessageHelper helper = new MimeMessageHelper(mimeMessage, true, "UTF-8");
            helper.setFrom(senderEmail);
            helper.setTo(toEmail);
            helper.setSubject("[내 요리를 부탁해!] 회원가입 이메일 인증");

            String verificationUrl = baseUrl + "/api/auth/verify-email?token=" + token;

            String htmlContent = "<html><body style='font-family: Arial, sans-serif;'>" +
                    "<h2>안녕하세요! 내 요리를 부탁해!에 오신 것을 환영합니다.</h2>" +
                    "<p>아래 버튼을 클릭하여 이메일 인증을 완료해주세요.</p>" +
                    "<a href='" + verificationUrl + "' style='" +
                    "display: inline-block; padding: 10px 20px; font-size: 16px; color: #ffffff; " +
                    "background-color: #4CAF50; text-decoration: none; border-radius: 5px;'>" +
                    "이메일 인증하기</a>" +
                    "</body></html>";

            helper.setText(htmlContent, true);

            mailSender.send(mimeMessage);
            log.info("인증 메일 발송 성공: email={}", toEmail);
        } catch (Exception e) {
            log.error("인증 메일 발송 실패: email={}, error={}", toEmail, e.getMessage());
            throw new RuntimeException("메일 발송 중 오류가 발생했습니다.");
        }
    }
}
