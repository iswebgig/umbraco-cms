using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Umbraco.Core.IO;
using Umbraco.Core.Services;

namespace Umbraco.Web.HealthCheck.Checks.Config
{
    public abstract class AbstractConfigCheck : HealthCheck
    {
        private readonly ConfigurationService _configurationService;

        protected ILocalizedTextService TextService { get; }

        /// <summary>
        /// Gets the config file path.
        /// </summary>
        public abstract string FilePath { get; }

        /// <summary>
        /// Gets XPath statement to the config element to check.
        /// </summary>
        public abstract string XPath { get; }

        /// <summary>
        /// Gets the values to compare against.
        /// </summary>
        public abstract IEnumerable<AcceptableConfiguration> Values { get; }

        /// <summary>
        /// Gets the current value
        /// </summary>
        public string CurrentValue { get; set; }

        /// <summary>
        /// Gets the provided value
        /// </summary>
        public string ProvidedValue { get; set; }

        /// <summary>
        /// Gets the comparison type for checking the value.
        /// </summary>
        public abstract ValueComparisonType ValueComparisonType { get; }

        /// <summary>
        /// Indicates validation method for provided value
        /// </summary>
        public virtual ProvidedValueValidation ProvidedValueValidation => ProvidedValueValidation.None;

        /// <summary>
        /// If provided value validation requires a regex, it's provided here
        /// </summary>
        public virtual string ProvidedValueValidationRegex => string.Empty;

        /// <summary>
        /// Gets the flag indicating if the check is considered successful if the config value is missing (defaults to false - an error - if missing)
        /// </summary>
        public virtual bool ValidIfConfigMissing => false;

        protected AbstractConfigCheck(ILocalizedTextService textService)
        {
            TextService = textService;
            _configurationService = new ConfigurationService(AbsoluteFilePath, XPath, textService);
        }

        /// <summary>
        /// Gets the name of the file.
        /// </summary>
        private string FileName => Path.GetFileName(FilePath);

        /// <summary>
        /// Gets the absolute file path.
        /// </summary>
        private string AbsoluteFilePath => IOHelper.MapPath(FilePath);

        /// <summary>
        /// Gets the message for when the check has succeeded.
        /// </summary>
        public virtual string CheckSuccessMessage
        {
            get
            {
                return TextService.Localize("healthcheck", "checkSuccessMessage",
                    new[] { CurrentValue, Values.First(v => v.IsRecommended).Value, XPath, AbsoluteFilePath  });
            }
        }

        /// <summary>
        /// Gets the message for when the check has failed.
        /// </summary>
        public virtual string CheckErrorMessage
        {
            get
            {
                return ValueComparisonType == ValueComparisonType.ShouldEqual
                    ? TextService.Localize("healthcheck", "checkErrorMessageDifferentExpectedValue",
                        new[] { CurrentValue, Values.First(v => v.IsRecommended).Value, XPath, AbsoluteFilePath })
                    : TextService.Localize("healthcheck", "checkErrorMessageUnexpectedValue",
                        new[] { CurrentValue, Values.First(v => v.IsRecommended).Value, XPath, AbsoluteFilePath });
            }
        }

        /// <summary>
        /// Gets the rectify success message.
        /// </summary>
        public virtual string RectifySuccessMessage
        {
            get
            {
                var recommendedValue = Values.FirstOrDefault(v => v.IsRecommended);
                var rectifiedValue = recommendedValue != null
                    ? recommendedValue.Value
                    : ProvidedValue;
                return TextService.Localize("healthcheck", "rectifySuccessMessage",
                    new[]
                    {
                        CurrentValue,
                        rectifiedValue,
                        XPath,
                        AbsoluteFilePath
                    });
            }
        }

        /// <summary>
        /// Gets a value indicating whether this check can be rectified automatically.
        /// </summary>
        public virtual bool CanRectify => ValueComparisonType == ValueComparisonType.ShouldEqual;

        /// <summary>
        /// Gets a value indicating whether this check can be rectified automatically if a value is provided.
        /// </summary>
        public virtual bool CanRectifyWithValue => ValueComparisonType == ValueComparisonType.ShouldNotEqual;

        public override IEnumerable<HealthCheckStatus> GetStatus()
        {
            var successMessage = string.Format(CheckSuccessMessage, FileName, XPath, Values);

            var configValue = _configurationService.GetConfigurationValue();
            if (configValue.Success == false)
            {
                if (ValidIfConfigMissing)
                {
                    return new[] { new HealthCheckStatus(successMessage) { ResultType = StatusResultType.Success } };
                }

                var errorMessage = configValue.Result;
                return new[] { new HealthCheckStatus(errorMessage) { ResultType = StatusResultType.Error } };
            }

            CurrentValue = configValue.Result;

            // need to update the successMessage with the CurrentValue
            successMessage = string.Format(CheckSuccessMessage, FileName, XPath, Values, CurrentValue);

            var valueFound = Values.Any(value => string.Equals(CurrentValue, value.Value, StringComparison.InvariantCultureIgnoreCase));
            if (ValueComparisonType == ValueComparisonType.ShouldEqual && valueFound || ValueComparisonType == ValueComparisonType.ShouldNotEqual && valueFound == false)
            {
                return new[] { new HealthCheckStatus(successMessage) { ResultType = StatusResultType.Success } };
            }

            // Declare the action for rectifying the config value
            var rectifyAction = new HealthCheckAction("rectify", Id)
            {
                Name = TextService.Localize("healthcheck", "rectifyButton"),
                ValueRequired = CanRectifyWithValue,
            };

            if (rectifyAction.ValueRequired)
            {
                rectifyAction.ProvidedValueValidation = ProvidedValueValidation.ToString().ToLower();
                rectifyAction.ProvidedValueValidationRegex = ProvidedValueValidationRegex;
            }

            var resultMessage = string.Format(CheckErrorMessage, FileName, XPath, Values, CurrentValue);
            return new[]
            {
                new HealthCheckStatus(resultMessage)
                {
                    ResultType = StatusResultType.Error,
                    Actions = CanRectify || CanRectifyWithValue ? new[] { rectifyAction } : new HealthCheckAction[0]
                }
            };
        }

        /// <summary>
        /// Rectifies this check.
        /// </summary>
        /// <returns></returns>
        public virtual HealthCheckStatus Rectify()
        {
            if (ValueComparisonType == ValueComparisonType.ShouldNotEqual)
                throw new InvalidOperationException(TextService.Localize("healthcheck", "cannotRectifyShouldNotEqual"));

            var recommendedValue = Values.First(v => v.IsRecommended).Value;
            return UpdateConfigurationValue(recommendedValue);
        }

        /// <summary>
        /// Rectifies this check with a provided value.
        /// </summary>
        /// <param name="value">Value provided</param>
        /// <returns></returns>
        public virtual HealthCheckStatus Rectify(string value)
        {
            if (ValueComparisonType == ValueComparisonType.ShouldEqual)
                throw new InvalidOperationException(TextService.Localize("healthcheck", "cannotRectifyShouldEqualWithValue"));

            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(TextService.Localize("healthcheck", "valueToRectifyNotProvided"));

            // Need to track provided value in order to correctly put together the rectify message
            ProvidedValue = value;

            return UpdateConfigurationValue(value);
        }

        private HealthCheckStatus UpdateConfigurationValue(string value)
        {
            var updateConfigFile = _configurationService.UpdateConfigFile(value);

            if (updateConfigFile.Success == false)
            {
                var message = updateConfigFile.Result;
                return new HealthCheckStatus(message) { ResultType = StatusResultType.Error };
            }

            var resultMessage = string.Format(RectifySuccessMessage, FileName, XPath, Values);
            return new HealthCheckStatus(resultMessage) { ResultType = StatusResultType.Success };
        }

        public override HealthCheckStatus ExecuteAction(HealthCheckAction action)
        {
            return string.IsNullOrEmpty(action.ProvidedValue)
                ? Rectify()
                : Rectify(action.ProvidedValue);
        }
    }
}
