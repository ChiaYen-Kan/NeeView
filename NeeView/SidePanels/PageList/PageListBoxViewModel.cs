﻿using NeeLaboratory.ComponentModel;
using NeeLaboratory.Windows.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace NeeView
{
    public class PageListBoxViewModel : BindableBase
    {
        private PageList _model;


        public PageListBoxViewModel(PageList model)
        {
            _model = model;
            _model.CollectionChanged += (s, e) =>
                AppDispatcher.Invoke(() => CollectionChanged?.Invoke(s, e));
        }


        public event EventHandler? CollectionChanged;

        public event EventHandler<ViewItemsChangedEventArgs>? ViewItemsChanged;


        public PageList Model => _model;
        
        /// <summary>
        /// 一度だけフォーカスするフラグ
        /// </summary>
        public bool FocusAtOnce { get; set; }


        public void Loaded()
        {
            _model.Loaded();
            _model.ViewItemsChanged += (s, e) => ViewItemsChanged?.Invoke(s, e);
        }

        public void Unloaded()
        {
            _model.Unloaded();
            _model.ViewItemsChanged -= (s, e) => ViewItemsChanged?.Invoke(s, e);
        }

        public bool IsLRKeyEnabled()
        {
            return Config.Current.Panels.IsLeftRightKeyEnabled || _model.PanelListItemStyle == PanelListItemStyle.Thumbnail;
        }
    }
}
