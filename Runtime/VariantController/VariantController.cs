using System;
using System.Linq;
using UnityEngine;

namespace Com.Hapiga.Scheherazade.MVOC
{

    public abstract class VariantController<TIndex, TVariant> : MonoBehaviour
        where TVariant : IObjectVariant<TIndex, TVariant>
        where TIndex : IEquatable<TIndex>
    {
        #region Interfaces
        public TVariant[] Variants => variants;
        public TVariant CurrentVariant => _currentVariant;
        public TIndex VariantIndex
        {
            get => _variantIndex;
            set
            {
                if (_variantIndex != null && _variantIndex.Equals(value)) return;

                _variantIndex = value;
                _updateVisual = true;
            }
        }
        #endregion

        #region Serialized Fields
        [SerializeField]
        private TVariant[] variants;
        #endregion

        #region Private Fields
        private TVariant _currentVariant;
        private TIndex _variantIndex;
        private bool _updateVisual;
        #endregion

        #region Unity Events
        protected virtual void Awake()
        {
            _updateVisual = true;

            foreach (TVariant variant in Variants)
            {
                if (variant.IsDefault)
                {
                    _currentVariant = variant;
                    _variantIndex = variant.Index;
                    break;
                }
            }
        }
        protected virtual void Update()
        {
            UpdateVariant();
        }
        #endregion

        #region Methods
        public void ForceUpdateVariant()
        {
            _updateVisual = true;
            UpdateVariant();
        }

        public void UpdateVariant()
        {
            if (!_updateVisual) return;
            _updateVisual = false;

            DisableCurrentVariant();
            TVariant compatibleVariant = default;

            if (VariantIndex != null)
            {
                compatibleVariant = Variants.FirstOrDefault(v => VariantIndex.Equals(v.Index));
            }

            if (compatibleVariant == null)
            {
                compatibleVariant = Variants.FirstOrDefault(v => v.IsDefault);
                return;
            }

            _currentVariant = compatibleVariant;

            if (_currentVariant != null)
            {
                EnableCurrentVariant();
            }
        }

        protected abstract void EnableCurrentVariant();
        protected abstract void DisableCurrentVariant();
        #endregion
    }

}