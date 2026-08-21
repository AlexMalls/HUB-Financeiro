using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HubFinanceiro;

public partial class SelecionarDataProvisionamentoWindow : Window
{
    public DateTime? DataSelecionada { get; private set; } = null;

    public SelecionarDataProvisionamentoWindow(List<DataProvisionamentoInfo> datas)
    {
        InitializeComponent();
        DatasItemsControl.ItemsSource = datas;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }

    private void DataItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border)
            return;

        if (border.Tag is not DataProvisionamentoInfo info)
            return;

        DataSelecionada = info.Data;
        DialogResult = true;
        Close();
    }

    private void Cancelar_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
