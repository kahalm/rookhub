-- Crawler DB + User
CREATE DATABASE IF NOT EXISTS `chessresults`
  CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS 'chessresults'@'%' IDENTIFIED BY 'chessresults';
GRANT ALL PRIVILEGES ON `chessresults`.* TO 'chessresults'@'%';

-- RookHub DB + User
CREATE DATABASE IF NOT EXISTS `rookhub`
  CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS 'rookhub'@'%' IDENTIFIED BY 'rookhub_secret';
GRANT ALL PRIVILEGES ON `rookhub`.* TO 'rookhub'@'%';

FLUSH PRIVILEGES;
