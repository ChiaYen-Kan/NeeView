﻿using System;
using System.Windows.Data;

namespace NeeView
{
    // バナーサーズ表示用コンバータ
    [ValueConversion(typeof(double), typeof(string))]
    public class BannerSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is double bannerSize)
            {
                return $"{(int)bannerSize}x{(int)(bannerSize / 4)}";
            }
            else
            {
                return value;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
