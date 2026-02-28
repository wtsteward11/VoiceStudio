// GAP-B15: Composite CanExecute for complex AND/OR conditions
// Enables fluent API for composing multiple CanExecute conditions.

using System;
using System.Collections.Generic;
using System.Linq;

namespace VoiceStudio.App.Core.Commands
{
    /// <summary>
    /// Mode for composing multiple conditions.
    /// </summary>
    public enum CompositionMode
    {
        /// <summary>All conditions must be true.</summary>
        And,

        /// <summary>At least one condition must be true.</summary>
        Or
    }

    /// <summary>
    /// Fluent builder for composing complex CanExecute conditions.
    /// GAP-B15: Supports AND/OR logic for command enablement.
    /// </summary>
    /// <remarks>
    /// Usage:
    /// <code>
    /// var canExecute = new CompositeCanExecute()
    ///     .And(() => HasSelectedClip)
    ///     .And(() => !IsBusy)
    ///     .Or(() => IsOverrideEnabled);
    ///
    /// AddToTimelineCommand = new RelayCommand(Execute, canExecute);
    /// </code>
    /// </remarks>
    public class CompositeCanExecute
    {
        private readonly List<Func<bool>> _conditions = new();
        private CompositionMode _mode = CompositionMode.And;

        /// <summary>
        /// Creates a new CompositeCanExecute with default AND mode.
        /// </summary>
        public CompositeCanExecute()
        {
        }

        /// <summary>
        /// Creates a new CompositeCanExecute with the specified mode.
        /// </summary>
        /// <param name="mode">The composition mode to use.</param>
        public CompositeCanExecute(CompositionMode mode)
        {
            _mode = mode;
        }

        /// <summary>
        /// Adds a condition with AND composition.
        /// All conditions must be true for the composite to evaluate true.
        /// </summary>
        /// <param name="condition">The condition to add.</param>
        /// <returns>This instance for fluent chaining.</returns>
        public CompositeCanExecute And(Func<bool> condition)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            _conditions.Add(condition);
            _mode = CompositionMode.And;
            return this;
        }

        /// <summary>
        /// Adds a condition with OR composition.
        /// At least one condition must be true for the composite to evaluate true.
        /// </summary>
        /// <param name="condition">The condition to add.</param>
        /// <returns>This instance for fluent chaining.</returns>
        public CompositeCanExecute Or(Func<bool> condition)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            _conditions.Add(condition);
            _mode = CompositionMode.Or;
            return this;
        }

        /// <summary>
        /// Evaluates all conditions using the current composition mode.
        /// </summary>
        /// <returns>True if the composite condition is satisfied.</returns>
        public bool Evaluate()
        {
            if (_conditions.Count == 0)
                return true;

            return _mode == CompositionMode.And
                ? _conditions.All(c => c())
                : _conditions.Any(c => c());
        }

        /// <summary>
        /// Gets the current composition mode.
        /// </summary>
        public CompositionMode Mode => _mode;

        /// <summary>
        /// Gets the count of registered conditions.
        /// </summary>
        public int ConditionCount => _conditions.Count;

        /// <summary>
        /// Implicit conversion to Func&lt;bool&gt; for use with RelayCommand.
        /// </summary>
        public static implicit operator Func<bool>(CompositeCanExecute composite)
        {
            if (composite == null) throw new ArgumentNullException(nameof(composite));
            return composite.Evaluate;
        }

        /// <summary>
        /// Creates a CompositeCanExecute that evaluates true when all conditions are true.
        /// </summary>
        public static CompositeCanExecute All(params Func<bool>[] conditions)
        {
            var composite = new CompositeCanExecute(CompositionMode.And);
            foreach (var condition in conditions)
            {
                composite.And(condition);
            }
            return composite;
        }

        /// <summary>
        /// Creates a CompositeCanExecute that evaluates true when any condition is true.
        /// </summary>
        public static CompositeCanExecute Any(params Func<bool>[] conditions)
        {
            var composite = new CompositeCanExecute(CompositionMode.Or);
            foreach (var condition in conditions)
            {
                composite.Or(condition);
            }
            return composite;
        }
    }
}
