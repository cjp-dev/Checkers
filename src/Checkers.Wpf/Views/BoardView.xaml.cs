using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Checkers.App.ViewModels;

namespace Checkers.Wpf.Views;

public partial class BoardView : UserControl
{
    private SquareViewModel? _dragSource;
    private Point _mouseDownPos;
    private bool _hasDragged;

    public BoardView()
    {
        InitializeComponent();
    }

    private void OnSquareButtonClick(object sender, RoutedEventArgs e)
    {
        // If a drag gesture was just completed, suppress button click
        if (_hasDragged) return;

        if (sender is Button { DataContext: SquareViewModel sq } && DataContext is MainViewModel vm)
        {
            vm.SquareClicked(sq);
        }
    }

    private void OnSquareMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Button { DataContext: SquareViewModel sq } && DataContext is MainViewModel vm)
        {
            vm.HoverSquare(sq);
        }
    }

    private void OnSquareMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Button { DataContext: SquareViewModel sq } && DataContext is MainViewModel vm)
        {
            vm.UnhoverSquare(sq);
        }
    }

    private void OnSquarePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button { DataContext: SquareViewModel sq })
        {
            _dragSource = sq;
            _mouseDownPos = e.GetPosition(this);
            _hasDragged = false;
        }
    }

    private void OnSquarePreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragSource != null && e.LeftButton == MouseButtonState.Pressed && !_hasDragged)
        {
            var currentPos = e.GetPosition(this);
            var diff = currentPos - _mouseDownPos;
            if (Math.Abs(diff.X) > 8 || Math.Abs(diff.Y) > 8)
            {
                _hasDragged = true;

                // When drag starts, select the source piece if it can move so valid target markers appear
                if (DataContext is MainViewModel vm && _dragSource.IsMovable)
                {
                    vm.SquareClicked(_dragSource);
                }
            }
        }
    }

    private void OnBoardPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_hasDragged && _dragSource != null && DataContext is MainViewModel vm)
        {
            var hit = VisualTreeHelper.HitTest(this, e.GetPosition(this));
            var targetSq = FindSquareViewModelFromHit(hit?.VisualHit);

            if (targetSq != null && targetSq != _dragSource)
            {
                vm.TryDragMove(_dragSource, targetSq);
            }

            _dragSource = null;
            _hasDragged = false;
            e.Handled = true;
            return;
        }

        _dragSource = null;
        _hasDragged = false;
    }

    private static SquareViewModel? FindSquareViewModelFromHit(DependencyObject? hit)
    {
        while (hit != null)
        {
            if (hit is FrameworkElement { DataContext: SquareViewModel sq })
            {
                return sq;
            }
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
    }
}
