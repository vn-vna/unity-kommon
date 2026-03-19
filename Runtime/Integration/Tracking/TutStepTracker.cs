using System.Collections.Generic;

namespace Com.Hapiga.Scheherazade.Common.Integration.Tracking
{
    // Custom event for tutorial steps
    // tut_step: tut_name, step_number, step_name, status_code
    public static class TutStepTracker
    {
        public static void TrackTutorialStep(string tutName, int stepNumber, string stepName)
        {
            if (string.IsNullOrEmpty(tutName)) tutName = "unknown";
            if (string.IsNullOrEmpty(stepName)) stepName = "unknown";

            var parameters = new Dictionary<string, object>
            {
                { "tut_name", tutName },
                { "step_number", stepNumber },
                { "step_name", stepName }
            };

            Integration.TrackingManager?.TrackAction(new TrackingActionInfo
            {
                ActionId = "tut_step",
                Parameters = parameters
            });
        }
    }
}
