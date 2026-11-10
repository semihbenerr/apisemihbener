-- Natro'daki yapıyı taklit eder: her uygulamanın ayrı veritabanı ve ayrı kullanıcısı var.
-- Bu dosya sadece volume ilk oluşturulduğunda çalışır.

CREATE DATABASE IF NOT EXISTS DB_QuizTactics CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE DATABASE IF NOT EXISTS DB_BidClash    CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE DATABASE IF NOT EXISTS DB_Football    CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;

CREATE USER IF NOT EXISTS 'quiztactics'@'%' IDENTIFIED BY 'dev_quiztactics';
CREATE USER IF NOT EXISTS 'bidclash'@'%'    IDENTIFIED BY 'dev_bidclash';
CREATE USER IF NOT EXISTS 'football'@'%'    IDENTIFIED BY 'dev_football';

GRANT ALL PRIVILEGES ON DB_QuizTactics.* TO 'quiztactics'@'%';
GRANT ALL PRIVILEGES ON DB_BidClash.*    TO 'bidclash'@'%';
GRANT ALL PRIVILEGES ON DB_Football.*    TO 'football'@'%';

FLUSH PRIVILEGES;
