﻿using NeeLaboratory.ComponentModel;

namespace NeeView
{
    /// <summary>
    /// NowLoading : Model
    /// </summary>
    public class NowLoading : BindableBase
    {
        static NowLoading() => Current = new NowLoading();
        public static NowLoading Current { get; }

        private NowLoading()
        {
            PageFrameBoxPresenter.Current.Loading +=
                (s, e) => IsDispNowLoading = e.Path != null;
        }

        /// <summary>
        /// IsDispNowLoading property.
        /// </summary>
        public bool IsDispNowLoading
        {
            get { return _IsDispNowLoading; }
            set { if (_IsDispNowLoading != value) { _IsDispNowLoading = value; RaisePropertyChanged(); } }
        }

        private bool _IsDispNowLoading;

        public void SetLoading(string message)
        {
            IsDispNowLoading = true;
        }

        public void ResetLoading()
        {
            IsDispNowLoading = false;
        }
    }

}
