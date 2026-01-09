using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Logging;
using Com.Hapiga.Scheherazade.Common.Singleton;

namespace Com.Hapiga.Scheherazade.Common
{
    /// <summary>
    /// Attribute to associate a command with a unique identifier.
    /// </summary>
    /// <example>
    /// <code>
    /// [CommandInfo(CommandId = "save-game")]
    /// public class SaveGameCommand : IManagedCommand
    /// {
    ///     // Implementation
    /// }
    /// </code>
    /// </example>
    public class CommandInfoAttribute : Attribute
    {
        /// <summary>
        /// Gets or sets the unique identifier for the command.
        /// </summary>
        public string CommandId { get; set; }
    }

    /// <summary>
    /// Represents the current status of a command's execution.
    /// </summary>
    public enum CommandStatus
    {
        /// <summary>
        /// The command is not ready to execute.
        /// </summary>
        NotReady,
        
        /// <summary>
        /// The command is ready to execute.
        /// </summary>
        Ready,
        
        /// <summary>
        /// The command is currently executing.
        /// </summary>
        Executing,
        
        /// <summary>
        /// The command has completed successfully.
        /// </summary>
        Completed,
        
        /// <summary>
        /// The command has failed during execution.
        /// </summary>
        Failed
    }

    /// <summary>
    /// Interface for commands that can be managed and executed by a command queue.
    /// </summary>
    /// <remarks>
    /// Implement this interface to create commands that can be queued and executed sequentially
    /// with status tracking and lifecycle events.
    /// </remarks>
    /// <example>
    /// <code>
    /// public class MyCommand : IManagedCommand
    /// {
    ///     public Action&lt;IManagedCommand&gt; Started { get; set; }
    ///     public Action&lt;IManagedCommand&gt; Completed { get; set; }
    ///     public Action&lt;IManagedCommand&gt; Failed { get; set; }
    ///     public CommandStatus Status { get; private set; } = CommandStatus.Ready;
    ///     
    ///     public void Execute()
    ///     {
    ///         Status = CommandStatus.Executing;
    ///         // Do work
    ///         Status = CommandStatus.Completed;
    ///     }
    /// }
    /// </code>
    /// </example>
    public interface IManagedCommand
    {
        /// <summary>
        /// Gets or sets the action invoked when command execution starts.
        /// </summary>
        Action<IManagedCommand> Started { get; set; }
        
        /// <summary>
        /// Gets or sets the action invoked when command execution completes successfully.
        /// </summary>
        Action<IManagedCommand> Completed { get; set; }
        
        /// <summary>
        /// Gets or sets the action invoked when command execution fails.
        /// </summary>
        Action<IManagedCommand> Failed { get; set; }

        /// <summary>
        /// Gets the current status of the command.
        /// </summary>
        CommandStatus Status { get; }
        
        /// <summary>
        /// Executes the command.
        /// </summary>
        void Execute();
    }

    /// <summary>
    /// Interface for commands that require contextual data for execution.
    /// </summary>
    /// <typeparam name="TContext">The type of context data required by the command.</typeparam>
    /// <example>
    /// <code>
    /// public class SavePlayerCommand : IContextualCommand&lt;PlayerData&gt;
    /// {
    ///     public PlayerData Context { get; set; }
    ///     // Implement other IManagedCommand members
    /// }
    /// </code>
    /// </example>
    public interface IContextualCommand<TContext> : IManagedCommand
    {
        /// <summary>
        /// Gets or sets the context data for this command.
        /// </summary>
        TContext Context { get; set; }
    }

    /// <summary>
    /// Interface for managing a queue of commands for sequential execution.
    /// </summary>
    /// <remarks>
    /// Command queues manage the lifecycle and execution order of commands,
    /// providing events for tracking command progress.
    /// </remarks>
    public interface ICommandQueue
    {
        /// <summary>
        /// Event raised when a command starts execution.
        /// </summary>
        event Action<IManagedCommand> CommandStarted;
        
        /// <summary>
        /// Event raised when a command completes successfully.
        /// </summary>
        event Action<IManagedCommand> CommandCompleted;
        
        /// <summary>
        /// Event raised when a command fails during execution.
        /// </summary>
        event Action<IManagedCommand> CommandFailed;

        /// <summary>
        /// Enqueues commands for execution.
        /// </summary>
        /// <param name="commands">The commands to enqueue.</param>
        void Enqueue(params IManagedCommand[] commands);
        
        /// <summary>
        /// Enqueues a collection of commands for execution.
        /// </summary>
        /// <param name="commands">The collection of commands to enqueue.</param>
        void Enqueue(IEnumerable<IManagedCommand> commands);
        
        /// <summary>
        /// Clears all pending commands from the queue.
        /// </summary>
        void Clear();
        
        /// <summary>
        /// Processes and executes queued commands.
        /// </summary>
        void ResolveCommands();
    }

    /// <summary>
    /// Interface for managing multiple command queues.
    /// </summary>
    public interface ICommandManager
    {
        /// <summary>
        /// Registers a command queue with the manager.
        /// </summary>
        /// <typeparam name="TQueue">The type of the command queue.</typeparam>
        /// <param name="queue">The queue to register.</param>
        void RegisterCommandQueue<TQueue>(TQueue queue) where TQueue : ICommandQueue;
        
        /// <summary>
        /// Unregisters a command queue from the manager.
        /// </summary>
        /// <typeparam name="TQueue">The type of the command queue.</typeparam>
        /// <param name="queue">The queue to unregister.</param>
        void UnregisterCommandQueue<TQueue>(TQueue queue) where TQueue : ICommandQueue;

        /// <summary>
        /// Clears all commands from all registered queues.
        /// </summary>
        void ClearAllQueues();
        
        /// <summary>
        /// Resolves commands in all registered queues.
        /// </summary>
        void ResolveAllQueues();
    }

    /// <summary>
    /// Base implementation of a simple command queue with sequential execution.
    /// </summary>
    /// <remarks>
    /// This class provides a basic command queue that processes commands one at a time,
    /// with configurable maximum commands per cycle to prevent long-running operations
    /// from blocking the main thread.
    /// </remarks>
    /// <example>
    /// <code>
    /// public class MyCommandQueue : SimpleCommandQueue
    /// {
    ///     public MyCommandQueue()
    ///     {
    ///         MaxCommandsPerCycle = 5;
    ///     }
    /// }
    /// 
    /// var queue = new MyCommandQueue();
    /// queue.Enqueue(new MyCommand());
    /// queue.ResolveCommands();
    /// </code>
    /// </example>
    public abstract class SimpleCommandQueue
        : ICommandQueue
    {
        /// <summary>
        /// Event raised when a command starts execution.
        /// </summary>
        public event Action<IManagedCommand> CommandStarted;
        
        /// <summary>
        /// Event raised when a command completes successfully.
        /// </summary>
        public event Action<IManagedCommand> CommandCompleted;
        
        /// <summary>
        /// Event raised when a command fails during execution.
        /// </summary>
        public event Action<IManagedCommand> CommandFailed;

        /// <summary>
        /// Gets the collection of pending commands.
        /// </summary>
        protected LinkedList<IManagedCommand> Commands => _commands;
        
        /// <summary>
        /// Gets the currently executing command, or null if no command is executing.
        /// </summary>
        protected IManagedCommand CurrentCommand => _currentCommand;
        
        /// <summary>
        /// Gets or sets the maximum number of commands to process per cycle.
        /// </summary>
        /// <remarks>
        /// This prevents long-running queue resolution from blocking the main thread.
        /// Default is 10.
        /// </remarks>
        protected int MaxCommandsPerCycle { get; set; } = 10;

        private readonly LinkedList<IManagedCommand> _commands;
        private IManagedCommand _currentCommand;

        /// <summary>
        /// Initializes a new instance of the SimpleCommandQueue class.
        /// </summary>
        protected SimpleCommandQueue()
        {
            _commands = new LinkedList<IManagedCommand>();
        }

        /// <summary>
        /// Clears all pending commands from the queue.
        /// </summary>
        public void Clear()
        {
            _commands.Clear();
        }

        /// <summary>
        /// Enqueues commands for execution.
        /// </summary>
        /// <param name="commands">The commands to enqueue.</param>
        public void Enqueue(params IManagedCommand[] commands)
        {
            Enqueue((IEnumerable<IManagedCommand>)commands);
        }

        /// <summary>
        /// Enqueues a collection of commands for execution.
        /// </summary>
        /// <param name="commands">The collection of commands to enqueue.</param>
        public void Enqueue(IEnumerable<IManagedCommand> commands)
        {
            foreach (var command in commands)
            {
                _commands.AddLast(command);
            }
        }

        /// <summary>
        /// Processes and executes queued commands up to MaxCommandsPerCycle.
        /// </summary>
        /// <remarks>
        /// This method processes commands sequentially, respecting the MaxCommandsPerCycle limit
        /// to prevent blocking. Call this repeatedly (e.g., in Update) to continue processing.
        /// </remarks>
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

    /// <summary>
    /// Singleton command manager for managing multiple command queues.
    /// </summary>
    /// <remarks>
    /// This is a stub implementation that can be extended to manage multiple command queues.
    /// </remarks>
    /// <example>
    /// <code>
    /// var manager = Commander.Instance;
    /// manager.RegisterCommandQueue(myQueue);
    /// manager.ResolveAllQueues();
    /// </code>
    /// </example>
    public class Commander :
        SingletonBehavior<Commander>,
        ICommandManager
    {
        /// <summary>
        /// Clears all commands from all registered queues (stub implementation).
        /// </summary>
        public void ClearAllQueues()
        { }

        /// <summary>
        /// Registers a command queue with the manager (stub implementation).
        /// </summary>
        /// <typeparam name="TQueue">The type of the command queue.</typeparam>
        /// <param name="queue">The queue to register.</param>
        public void RegisterCommandQueue<TQueue>(TQueue queue) where TQueue : ICommandQueue
        { }

        /// <summary>
        /// Resolves commands in all registered queues (stub implementation).
        /// </summary>
        public void ResolveAllQueues()
        { }

        /// <summary>
        /// Unregisters a command queue from the manager (stub implementation).
        /// </summary>
        /// <typeparam name="TQueue">The type of the command queue.</typeparam>
        /// <param name="queue">The queue to unregister.</param>
        public void UnregisterCommandQueue<TQueue>(TQueue queue) where TQueue : ICommandQueue
        { }
    }
}