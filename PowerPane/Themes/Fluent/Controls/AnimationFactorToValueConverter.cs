// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported to C# 7.3 for PowerPane's .NET Framework 4.8 Fluent theme port:
// `is not` (C# 9) rewritten as a type check plus an explicit cast.
using System;
using System.Windows.Data;
using System.Globalization;

namespace Fluent.Controls
{
    internal class AnimationFactorToValueConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(values[0] is double))
            {
                return 0.0;
            }
            double completeValue = (double)values[0];

            if (!(values[1] is double))
            {
                return 0.0;
            }
            double factor = (double)values[1];
            if (factor == double.NaN)
            {
                return 0.0;
            }

            if (parameter is "negative")
            {
                factor = -factor;
            }

            return factor * completeValue;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
