using log4net;
using log4net.Config;
using log4net.Core;
using log4net.Repository.Hierarchy;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
namespace ParkingHelp.Logging
{
    public static class Logs
    {
        private static bool _loggingEnabled = true;
        private static readonly Dictionary<string, ILog> _loggerCache = new();

        public static void Init(IConfiguration configuration = null)
        {
            try
            {
                string logDirectory = Environment.CurrentDirectory + "\\Logs";
                string configFile = Environment.CurrentDirectory + "\\log4net.config";
                // 1. 로그 폴더 생성
                if (!Directory.Exists(logDirectory))
                    Directory.CreateDirectory(logDirectory);

                Console.WriteLine($"logDirectory : " + logDirectory);
                Console.WriteLine($"log4net.config : " + configFile);
                // 2. 설정 파일 로드
                var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
                XmlConfigurator.Configure(logRepository, new FileInfo(configFile));

                // 3. 설정값에서 로그 활성화 여부 확인
                foreach (var appender in logRepository.GetAppenders())
                {
                    if (appender is log4net.Appender.RollingFileAppender rollingFileAppender)
                    {
                        rollingFileAppender.File = Path.Combine(logDirectory, "log_");
                        rollingFileAppender.ActivateOptions(); // 필수! 변경사항 반영
                        Console.WriteLine("log4net appender 경로 적용됨: " + rollingFileAppender.File);
                    }
                }

                // 설정에서 로깅 활성화 여부
                if (configuration != null)
                {
                    var enabled = configuration["LoggingEnabled"];
                    if (!string.IsNullOrWhiteSpace(enabled) && bool.TryParse(enabled, out var result))
                    {
                        _loggingEnabled = result;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Log Init Error] {ex.Message}");
            }
        }

        // 4. 호출자 기준 Logger 자동 추출
        private static ILog GetCallerLogger([CallerFilePath] string sourceFilePath = "")
        {
            if (!_loggingEnabled)
                return NoOpLogger.Instance;

            var key = Path.GetFileNameWithoutExtension(sourceFilePath); // ex: MyService.cs → MyService

            if (_loggerCache.TryGetValue(key, out var logger))
                return logger;

            logger = LogManager.GetLogger(key); // string name 기반으로 생성
            _loggerCache[key] = logger;
            return logger;
        }

        // 5. 호출 메서드
        public static void Info(string message) => GetCallerLogger().Info(message);
        public static void Debug(string message) => GetCallerLogger().Debug(message);
        public static void Warn(string message) => GetCallerLogger().Warn(message);
        public static void Error(string message, Exception ex = null) => GetCallerLogger().Error(message, ex);
    }

}