using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    // Custom event for login loading steps
    public static class LoginLoadingTracker
    {
        public static void TrackLoginLoadingStep(int stepNumber, string stepName, int statusCode)
        {
            if (string.IsNullOrEmpty(stepName)) stepName = "unknown";

            var parameters = new Dictionary<string, object>
            {
                { "step_number", stepNumber },
                { "step_name", stepName },
                { "status_code", statusCode }
            };

            Integration.TrackingManager?.TrackAction(new TrackingActionInfo
            {
                ActionId = "login_loading_step",
                Parameters = parameters
            });
        }
    }
}
