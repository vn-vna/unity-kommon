using System;
using System.Collections.Generic;

namespace Com.Scheherazade.Common.Frameworks.SandEngine
{
    public partial class SandEngine
    {
        private readonly HashSet<int> _removalValidation = new HashSet<int>();

        /// <summary>
        /// Commits an engine-local reservation batch in caller order, then repairs
        /// vacancies once. All indices must be unique, still contain the reserved
        /// sand, and belong to this engine. Invalid batches fail before mutation
        /// or sample RNG consumption. Complete scheduled work before selecting.
        /// The caller owns selection, budgets, history and external accounting.
        /// </summary>
        public int CommitReservedRemovals(
            IReadOnlyList<SandRemovalSample> reservations,
            SandRemovalSampleBuffer samples = null
        )
        {
            if (reservations == null)
            {
                throw new ArgumentNullException(nameof(reservations));
            }
            if (reservations.Count == 0)
            {
                return 0;
            }

            CompleteStep();
            ValidateReservations(reservations);
            BeginCollectionVacancyRepair();
            for (int i = 0; i < reservations.Count; i++)
            {
                SandRemovalSample reservation = reservations[i];
                samples?.Consider(reservation.CellIndex, reservation.PackedCell);
                Backend.SetCell(reservation.CellIndex, 0);
                TrackCollectionVacancy(reservation.CellIndex, reservation.PackedCell);
            }
            return CompleteCollectionVacancyRepair(reservations.Count);
        }

        private void ValidateReservations(IReadOnlyList<SandRemovalSample> reservations)
        {
            _removalValidation.Clear();
            try
            {
                for (int i = 0; i < reservations.Count; i++)
                {
                    SandRemovalSample reservation = reservations[i];
                    if (reservation.CellIndex < 0
                        || reservation.CellIndex >= Backend.CellCount
                        || !_removalValidation.Add(reservation.CellIndex)
                        || SandCell.GetElementType(reservation.PackedCell) != ElementType.Sand
                        || Backend.GetCell(reservation.CellIndex) != reservation.PackedCell)
                    {
                        throw new ArgumentException("Invalid or stale sand reservation batch.", nameof(reservations));
                    }
                }
            }
            finally
            {
                _removalValidation.Clear();
            }
        }
    }
}
