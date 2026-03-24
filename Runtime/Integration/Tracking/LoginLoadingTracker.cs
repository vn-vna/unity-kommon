using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    // Custom event for login loading steps
    public static class LoginLoadingTracker
    {
        private static readonly Queue<LoginLoadingStepData> _pendingSteps = new();

        public static void EnqueueStep(int stepNumber, string stepName, int statusCode)
        {
            if (string.IsNullOrEmpty(stepName)) stepName = "unknown";

            _pendingSteps.Enqueue(new LoginLoadingStepData
            {
                StepNumber = stepNumber,
                StepName = stepName,
                StatusCode = statusCode
            });
        }

        public static void Flush()
        {
            while (_pendingSteps.Count > 0)
            {
                var step = _pendingSteps.Dequeue();

                var parameters = new Dictionary<string, object>
            {
                { "step_number", step.StepNumber },
                { "step_name", step.StepName },
                { "status_code", step.StatusCode }
            };

                Integration.TrackingManager?.TrackAction(new TrackingActionInfo
                {
                    ActionId = "login_loading_step",
                    Parameters = parameters
                });
            }
        }

        public static void Clear()
        {
            _pendingSteps.Clear();
        }
    }
    public class LoginLoadingStepData
    {
        public int StepNumber;
        public string StepName;
        public int StatusCode;
    }
}
