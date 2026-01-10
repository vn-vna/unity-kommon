using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;

namespace Com.Hapiga.Scheherazade.Common
{
    public class CommandInfoAttribute : Attribute
    {
        public string CommandId { get; set; }
    }

    public enum CommandStatus
    {
        NotReady,
        Ready,
        Executing,
        Completed,
        Failed
    }

    public interface IManagedCommand
    {
        Action<IManagedCommand> Started { get; set; }
        Action<IManagedCommand> Completed { get; set; }
        Action<IManagedCommand> Failed { get; set; }

        CommandStatus Status { get; }
        void Execute();
    }

    public interface IContextualCommand<TContext> : IManagedCommand
    {
        TContext Context { get; set; }
    }

    public interface ICommandQueue
    {
        event Action<IManagedCommand> CommandStarted;
        event Action<IManagedCommand> CommandCompleted;
        event Action<IManagedCommand> CommandFailed;

        void Enqueue(params IManagedCommand[] commands);
        void Enqueue(IEnumerable<IManagedCommand> commands);
        void Clear();
        void ResolveCommands();
    }

    public interface ICommandManager
    {
        void RegisterCommandQueue<TQueue>(TQueue queue) where TQueue : ICommandQueue;
        void UnregisterCommandQueue<TQueue>(TQueue queue) where TQueue : ICommandQueue;

        void ClearAllQueues();
        void ResolveAllQueues();
    }

    public abstract class SimpleCommandQueue
        : ICommandQueue
    {
        public event Action<IManagedCommand> CommandStarted;
        public event Action<IManagedCommand> CommandCompleted;
        public event Action<IManagedCommand> CommandFailed;

        protected LinkedList<IManagedCommand> Commands => _commands;
        protected IManagedCommand CurrentCommand => _currentCommand;
        protected int MaxCommandsPerCycle { get; set; } = 10;

        private readonly LinkedList<IManagedCommand> _commands;
        private IManagedCommand _currentCommand;

        protected SimpleCommandQueue()
        {
            _commands = new LinkedList<IManagedCommand>();
        }

        public void Clear()
        {
            _commands.Clear();
        }

        public void Enqueue(params IManagedCommand[] commands)
        {
            Enqueue((IEnumerable<IManagedCommand>)commands);
        }

        public void Enqueue(IEnumerable<IManagedCommand> commands)
        {
            foreach (var command in commands)
            {
                _commands.AddLast(command);
            }
        }

        public void ResolveCommands()
        {
            bool continueResolving = true;
            int commandStartedCount = 0;

            while (
                continueResolving && 
                commandStartedCount < MaxCommandsPerCycle
            )
            {
                if (_currentCommand == null && !FindNextCommand())
                {
                    break;
                }

                commandStartedCount++;

                try
                {
                    continueResolving = HandleCurrentCommand();
                }
                catch (Exception ex)
                {
                    continueResolving = false;
                    QuickLog.Error<SimpleCommandQueue>(
                        "Exception occurred while resolving command '{0}': {1}",
                        _currentCommand.GetType().Name,
                        ex
                    );
                    NotifyCommandFailed();
                }
            }
        }

        private bool HandleCurrentCommand()
        {
            switch (_currentCommand.Status)
            {
                case CommandStatus.Ready:
                    NotifyCommandStarted();
                    _currentCommand.Execute();
                    return true;

                case CommandStatus.Completed:
                    NotifyCommandCompleted();
                    return true;

                case CommandStatus.Failed:
                    NotifyCommandFailed();
                    return true;
            }

            return false;
        }

        private void NotifyCommandFailed()
        {
            InvokeActionSafely(CommandFailed, _currentCommand);
            InvokeActionSafely(_currentCommand.Failed, _currentCommand);
            _currentCommand = null;
        }

        private void NotifyCommandCompleted()
        {
            InvokeActionSafely(CommandCompleted, _currentCommand);
            InvokeActionSafely(_currentCommand.Completed, _currentCommand);
            _currentCommand = null;
        }

        private void NotifyCommandStarted()
        {
            InvokeActionSafely(CommandStarted, _currentCommand);
            InvokeActionSafely(_currentCommand.Started, _currentCommand);
        }

        private void InvokeActionSafely(Action<IManagedCommand> action, IManagedCommand command)
        {
            try
            {
                action?.Invoke(command);
            }
            catch (Exception ex)
            {
                QuickLog.Error<SimpleCommandQueue>(
                    "Exception occurred while invoking action for command '{0}': {1}",
                    command.GetType().Name,
                    ex
                );
            }
        }

        private bool FindNextCommand()
        {
            if (_commands.Count == 0)
            {
                return false;
            }

            _currentCommand = _commands.First.Value;
            _commands.RemoveFirst();
            return true;
        }
    }

    public class Commander :
        SingletonBehavior<Commander>,
        ICommandManager
    {
        public void ClearAllQueues()
        { }

        public void RegisterCommandQueue<TQueue>(TQueue queue) where TQueue : ICommandQueue
        { }

        public void ResolveAllQueues()
        { }

        public void UnregisterCommandQueue<TQueue>(TQueue queue) where TQueue : ICommandQueue
        { }
    }
}