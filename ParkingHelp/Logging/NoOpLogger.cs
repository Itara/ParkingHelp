﻿using log4net;
using log4net.Core;
using System;

namespace ParkingHelp.Logging
{
    public class NoOpLogger : ILog, ILoggerWrapper
    {
        public static readonly ILog Instance = new NoOpLogger();

        public bool IsDebugEnabled => false;
        public bool IsInfoEnabled => false;
        public bool IsWarnEnabled => false;
        public bool IsErrorEnabled => false;
        public bool IsFatalEnabled => false;

        public log4net.Core.ILogger Logger => null;

        public void Debug(object message) { }
        public void Debug(object message, Exception exception) { }
        public void DebugFormat(string format, params object[] args) { }
        public void DebugFormat(string format, object arg0) { }
        public void DebugFormat(string format, object arg0, object arg1) { }
        public void DebugFormat(string format, object arg0, object arg1, object arg2) { }
        public void DebugFormat(IFormatProvider provider, string format, params object[] args) { }

        public void Info(object message) { }
        public void Info(object message, Exception exception) { }
        public void InfoFormat(string format, params object[] args) { }
        public void InfoFormat(string format, object arg0) { }
        public void InfoFormat(string format, object arg0, object arg1) { }
        public void InfoFormat(string format, object arg0, object arg1, object arg2) { }
        public void InfoFormat(IFormatProvider provider, string format, params object[] args) { }

        public void Warn(object message) { }
        public void Warn(object message, Exception exception) { }
        public void WarnFormat(string format, params object[] args) { }
        public void WarnFormat(string format, object arg0) { }
        public void WarnFormat(string format, object arg0, object arg1) { }
        public void WarnFormat(string format, object arg0, object arg1, object arg2) { }
        public void WarnFormat(IFormatProvider provider, string format, params object[] args) { }

        public void Error(object message) { }
        public void Error(object message, Exception exception) { }
        public void ErrorFormat(string format, params object[] args) { }
        public void ErrorFormat(string format, object arg0) { }
        public void ErrorFormat(string format, object arg0, object arg1) { }
        public void ErrorFormat(string format, object arg0, object arg1, object arg2) { }
        public void ErrorFormat(IFormatProvider provider, string format, params object[] args) { }

        public void Fatal(object message) { }
        public void Fatal(object message, Exception exception) { }
        public void FatalFormat(string format, params object[] args) { }
        public void FatalFormat(string format, object arg0) { }
        public void FatalFormat(string format, object arg0, object arg1) { }
        public void FatalFormat(string format, object arg0, object arg1, object arg2) { }
        public void FatalFormat(IFormatProvider provider, string format, params object[] args) { }
    }
}