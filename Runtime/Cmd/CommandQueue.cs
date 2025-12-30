using System;
using System.Collections.Generic;
using Com.Hapiga.Scheherazade.Common.Logging;

namespace Com.Hapiga.Scheherazade.Common
{
    public interface ICommand
    {
        Type QueueType { get; }
        void Execute(ICommandQueue queue);
    }

    public interface ICommand<TQueue> : ICommand
        where TQueue : ICommandQueue<TQueue>
    {
        new Type QueueType => typeof(TQueue);
        void Dispatch(TQueue queue) => queue.Enqueue(this);
    }

    public interface ICommandQueue
    {
        void ResolveCommands();
    }

    public interface ICommandQueue<TQueue> : ICommandQueue
        where TQueue : ICommandQueue<TQueue>
    {
        Queue<ICommand<TQueue>> Commands { get; }
        LinkedList<ICommand<TQueue>> History { get; }

        void Enqueue(ICommand<TQueue> command);
    }

    public abstract class CommandQueue : ICommandQueue<CommandQueue>
    {
        public event Action<ICommand<CommandQueue>> CommandExecuted;
        public event Action CommandResolutionStarted;
        public event Action CommandResolutionCompleted;

        public Queue<ICommand<CommandQueue>> Commands { get; private set; }

        public LinkedList<ICommand<CommandQueue>> History { get; private set; }

        public int MaxHistoryLength { get; set; } = 100;

        public CommandQueue()
        {
            Commands = new Queue<ICommand<CommandQueue>>();
            History = new LinkedList<ICommand<CommandQueue>>();
        }

        public void Enqueue(ICommand<CommandQueue> command)
        {
            Commands.Enqueue(command);
        }

        public void ResolveCommands()
        {
            CommandResolutionCompleted?.Invoke();
            while (Commands.Count > 0)
            {
                var command = Commands.Dequeue();
                try
                {
                    HandleCommandExecution(command);
                }
                catch (Exception e)
                {
                    QuickLog.Error<ICommandQueue<CommandQueue>>(
                        "Error executing command of type {0}: {1}",
                        command.GetType().Name,
                        e.Message
                    );
                }
            }
            CommandResolutionStarted?.Invoke();
        }

        private void HandleCommandExecution(ICommand<CommandQueue> command)
        {
            if (command == null) return;
            if (command.QueueType != typeof(CommandQueue))
            {
                QuickLog.Error<ICommandQueue<CommandQueue>>(
                    "Cannot execute command of type {0} on queue of type {1}.",
                    command.GetType().Name,
                    typeof(CommandQueue).Name
                );
                return;
            }

            command.Execute(this);
            History.AddLast(command);

            if (History.Count > MaxHistoryLength)
            {
                History.RemoveFirst();
            }

            CommandExecuted?.Invoke(command);
        }
    }
}