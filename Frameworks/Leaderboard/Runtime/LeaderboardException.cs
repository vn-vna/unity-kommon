using System;

namespace Com.Hapiga.Scheherazade.Common.Leaderboard
{
    public class LeaderboardException : Exception
    {
        public LeaderboardException(string message) : base(message) { }
        public LeaderboardException(string message, Exception inner) : base(message, inner) { }
    }

    public class ProviderException : LeaderboardException
    {
        public string ProviderId { get; }

        public ProviderException(string providerId, string message, Exception inner = null)
            : base(message, inner)
        {
            ProviderId = providerId;
        }
    }

    public class ProviderInitializationException : ProviderException
    {
        public ProviderInitializationException(string providerId, string message, Exception inner = null)
            : base(providerId, message, inner) { }
    }

    public class ScoreSubmissionException : LeaderboardException
    {
        public string LeaderboardId { get; }
        public long Score { get; }

        public ScoreSubmissionException(
            string leaderboardId,
            long score,
            string message,
            Exception inner = null
        ) : base(message, inner)
        {
            LeaderboardId = leaderboardId;
            Score = score;
        }
    }

    public class LeaderboardNotFoundException : LeaderboardException
    {
        public string LeaderboardId { get; }

        public LeaderboardNotFoundException(string leaderboardId)
            : base($"Leaderboard not found: '{leaderboardId}'")
        {
            LeaderboardId = leaderboardId;
        }
    }

    public class AuthenticationException : LeaderboardException
    {
        public AuthenticationException(string message, Exception inner = null)
            : base(message, inner) { }
    }

    public class LeaderboardNotInitializedException : LeaderboardException
    {
        public LeaderboardNotInitializedException()
            : base("Leaderboard is not initialized. Call LeaderboardDirector.Instance.Initialize() first.") { }

        public LeaderboardNotInitializedException(string message)
            : base(message) { }
    }
}
