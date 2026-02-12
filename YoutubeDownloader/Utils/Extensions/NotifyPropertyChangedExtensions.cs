using System;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using YoutubeDownloader.Utils;

namespace YoutubeDownloader.Utils.Extensions;

internal static class NotifyPropertyChangedExtensions
{
    public static IDisposable WatchProperty<TOwner, TProperty>(
        this TOwner owner,
        Expression<Func<TOwner, TProperty>> propertyExpression,
        Action callback,
        bool watchInitialValue = false
    )
        where TOwner : INotifyPropertyChanged
    {
        var memberExpression = propertyExpression.Body as MemberExpression;
        if (memberExpression?.Member is not PropertyInfo property)
            throw new ArgumentException("Provided expression must reference a property.");

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (
                string.IsNullOrWhiteSpace(args.PropertyName)
                || string.Equals(args.PropertyName, property.Name, StringComparison.Ordinal)
            )
            {
                callback();
            }
        }

        owner.PropertyChanged += OnPropertyChanged;

        if (watchInitialValue)
            callback();

        return Disposable.Create(() => owner.PropertyChanged -= OnPropertyChanged);
    }

    public static IDisposable WatchAllProperties(
        this INotifyPropertyChanged owner,
        Action callback,
        bool watchInitialValues = false
    )
    {
        void OnPropertyChanged(object? sender, PropertyChangedEventArgs args) => callback();
        owner.PropertyChanged += OnPropertyChanged;

        if (watchInitialValues)
            callback();

        return Disposable.Create(() => owner.PropertyChanged -= OnPropertyChanged);
    }
}
