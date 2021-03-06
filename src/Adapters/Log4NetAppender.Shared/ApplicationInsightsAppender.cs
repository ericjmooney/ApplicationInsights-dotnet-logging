﻿// -----------------------------------------------------------------------
// <copyright file="ApplicationInsightsAppender.cs" company="Microsoft">
// Copyright (c) Microsoft Corporation. 
// All rights reserved.  2013
// </copyright>
// -----------------------------------------------------------------------

namespace Microsoft.ApplicationInsights.Log4NetAppender
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Reflection;

    using log4net.Appender;
    using log4net.Core;

    using Microsoft.ApplicationInsights.Channel;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.Extensibility.Implementation;

    /// <summary>
    /// Log4Net Appender that routes all logging output to the Application Insights logging framework.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable",
        Justification = "Releasing the resources on the close method")]
    public sealed class ApplicationInsightsAppender : AppenderSkeleton
    {
        private TelemetryClient telemetryClient;

        /// <summary>
        /// Get/set The Application Insights instrumentationKey for your application. 
        /// </summary>
        /// <remarks>
        /// This is normally pushed from when Appender is being initialized.
        /// </remarks>
        public string InstrumentationKey { get; set; }

        internal TelemetryClient TelemetryClient
        {
            get { return this.telemetryClient; }
        }

        /// <summary>
        /// The <see cref="ApplicationInsightsAppender"/> requires a layout.
        /// This Appender converts the LoggingEvent it receives into a text string and requires the layout format string to do so.
        /// </summary>
        protected override bool RequiresLayout
        {
            get { return true; }
        }

        /// <summary>
        /// Initializes the Appender and perform instrumentationKey validation.
        /// </summary>
        public override void ActivateOptions()
        {
            base.ActivateOptions();
            this.telemetryClient = new TelemetryClient();
            if (!string.IsNullOrEmpty(this.InstrumentationKey))
            {
                this.telemetryClient.Context.InstrumentationKey = this.InstrumentationKey;
            }

            this.telemetryClient.Context.GetInternalContext().SdkVersion = "Log4Net:" + GetAssemblyVersion();
        }

        /// <summary>
        /// Append LoggingEvent Application Insights logging framework.
        /// </summary>
        /// <param name="loggingEvent">Events to be logged.</param>
        protected override void Append(LoggingEvent loggingEvent)
        {
            if (loggingEvent.ExceptionObject != null)
            {
                this.SendException(loggingEvent);
            }
            else
            {
                this.SendTrace(loggingEvent);
            }
        }

        private static string GetAssemblyVersion()
        {
            return typeof(ApplicationInsightsAppender).Assembly.GetCustomAttributes(false)
                    .OfType<AssemblyFileVersionAttribute>()
                    .First()
                    .Version;
        }

        private static void AddLoggingEventProperty(string key, string value, IDictionary<string, string> metaData)
        {
            if (value != null)
            {
                metaData.Add(key, value);
            }
        }

        private void SendException(LoggingEvent loggingEvent)
        {
            try
            {
                var exceptionTelemetry = new ExceptionTelemetry(loggingEvent.ExceptionObject)
                {
                    SeverityLevel = this.GetSeverityLevel(loggingEvent.Level)
                };

                this.BuildCustomProperties(loggingEvent, exceptionTelemetry);
                this.telemetryClient.Track(exceptionTelemetry);
            }
            catch (ArgumentNullException exception)
            {
                throw new LogException(exception.Message, exception);
            }
        }

        private void SendTrace(LoggingEvent loggingEvent)
        {
            try
            {
                loggingEvent.GetProperties();
                string message = loggingEvent.RenderedMessage != null ? this.RenderLoggingEvent(loggingEvent) : "Log4Net Trace";

                var trace = new TraceTelemetry(message)
                {
                    SeverityLevel = this.GetSeverityLevel(loggingEvent.Level)
                };

                this.BuildCustomProperties(loggingEvent, trace);
                this.telemetryClient.Track(trace);
            }
            catch (ArgumentNullException exception)
            {
                throw new LogException(exception.Message, exception);
            }
        }

        private void BuildCustomProperties(LoggingEvent loggingEvent, ITelemetry trace)
        {
            trace.Timestamp = loggingEvent.TimeStamp;
            trace.Context.User.Id = loggingEvent.UserName;

            IDictionary<string, string> metaData;
            
            if (trace is ExceptionTelemetry)
            {
                metaData = ((ExceptionTelemetry)trace).Properties;
            }
            else
            {
                metaData = ((TraceTelemetry)trace).Properties;
            }

            AddLoggingEventProperty("LoggerName", loggingEvent.LoggerName, metaData);
            AddLoggingEventProperty("ThreadName", loggingEvent.ThreadName, metaData);

            var locationInformation = loggingEvent.LocationInformation;
            if (locationInformation != null)
            {
                AddLoggingEventProperty("ClassName", locationInformation.ClassName, metaData);
                AddLoggingEventProperty("FileName", locationInformation.FileName, metaData);
                AddLoggingEventProperty("MethodName", locationInformation.MethodName, metaData);
                AddLoggingEventProperty("LineNumber", locationInformation.LineNumber, metaData);
            }
            
            AddLoggingEventProperty("Domain", loggingEvent.Domain, metaData);
            AddLoggingEventProperty("Identity", loggingEvent.Identity, metaData);

            var properties = loggingEvent.GetProperties();
            if (properties != null)
            {
                foreach (string key in properties.GetKeys())
                {
                    if (!string.IsNullOrEmpty(key) && !key.StartsWith("log4net", StringComparison.OrdinalIgnoreCase))
                    {
                        object value = properties[key];
                        if (value != null)
                        {
                            AddLoggingEventProperty(key, value.ToString(), metaData);
                        }
                    }
                }
            }
        }

        private SeverityLevel? GetSeverityLevel(Level logginEventLevel)
        {
            if (logginEventLevel == null)
            {
                return null;
            }

            if (logginEventLevel.Value < Level.Info.Value)
            {
                return SeverityLevel.Verbose;
            }

            if (logginEventLevel.Value < Level.Warn.Value)
            {
                return SeverityLevel.Information;
            }

            if (logginEventLevel.Value < Level.Error.Value)
            {
                return SeverityLevel.Warning;
            }

            if (logginEventLevel.Value < Level.Severe.Value)
            {
                return SeverityLevel.Error;
            }

            return SeverityLevel.Critical;
        }
    }
}
