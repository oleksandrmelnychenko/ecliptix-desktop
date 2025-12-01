using System;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Features.NewContent.ViewModels;
using ReactiveUI;

namespace Ecliptix.Core.Features.NewContent.Views;

public partial class CreateWizardView : ReactiveUserControl<CreateWizardViewModel>
{
    private Border? _sizingContainer;
    private Canvas? _measureContainer;

    public CreateWizardView()
    {
        InitializeComponent();

        // Підписуємось на активацію View (коли вона з'являється на екрані)
        this.WhenActivated(disposables =>
        {
            _sizingContainer = this.FindControl<Border>("SizingContainer");
            _measureContainer = this.FindControl<Canvas>("HiddenMeasureContainer");

            // Слухаємо зміну поточної сторінки у ViewModel
            ViewModel!.WhenAnyValue(x => x.CurrentPage)
                .Subscribe(OnPageChanged)
                .DisposeWith(disposables);
        });
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnPageChanged(object? newPageViewModel)
    {
        if (newPageViewModel == null || _sizingContainer == null || _measureContainer == null)
        {
            return;
        }

        // 1. Створюємо View для нової ViewModel
        // Оскільки у нас DataTemplates в XAML, тут ми вручну мапимо типи
        // для цілей вимірювання.
        Control? viewToMeasure = CreateViewForViewModel(newPageViewModel);

        if (viewToMeasure == null)
        {
            return;
        }

        // 2. Вимірюємо розмір
        _measureContainer.Children.Add(viewToMeasure);

        // Size.Infinity дозволяє контенту зайняти стільки місця, скільки йому треба
        viewToMeasure.Measure(Size.Infinity);

        Size desiredSize = viewToMeasure.DesiredSize;

        // Прибираємо з контейнера вимірювання
        _measureContainer.Children.Remove(viewToMeasure);

        // 3. Задаємо розміри контейнеру.
        // Оскільки у Border є Transitions, він плавно анімується до нових значень.

        // Якщо це перший показ (Width is NaN), ставимо миттєво без анімації,
        // або використовуємо Dispatcher для плавності.

        if (double.IsNaN(_sizingContainer.Width))
        {
             // Перший рендер - ставимо жорстко, щоб не було анімації "з нуля"
            _sizingContainer.Width = desiredSize.Width;
            _sizingContainer.Height = desiredSize.Height;
        }
        else
        {
            // Наступні переходи - анімуємо
            _sizingContainer.Width = desiredSize.Width;
            _sizingContainer.Height = desiredSize.Height;
        }
    }

    // Фабричний метод для створення View (мапінг)
    // Це потрібно тільки для пре-калькуляції розміру
    private Control? CreateViewForViewModel(object viewModel)
    {
        return viewModel switch
        {
            CreateSelectionViewModel => new CreateSelectionView { DataContext = viewModel },
            NewChannelViewModel => new NewChannelView { DataContext = viewModel },
            NewGroupChatViewModel => new NewGroupChatView { DataContext = viewModel },
            NewPostViewModel => new NewPostView { DataContext = viewModel },
            NewContactViewModel => new NewContactView { DataContext = viewModel },
            _ => null
        };
    }
}
