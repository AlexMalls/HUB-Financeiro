$ErrorActionPreference = 'Stop'

git config core.autocrlf false
git fetch origin 7f20a5551722d14d94f9c1afa5c28bb1a2fe420d --depth=1

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = 'git'
$psi.Arguments = 'cat-file blob 2acc92c2d7de01a0cb974d50cd2d49bc7f44eeda'
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.CreateNoWindow = $true
$proc = New-Object System.Diagnostics.Process
$proc.StartInfo = $psi
[void]$proc.Start()
$stream = New-Object System.IO.FileStream('MainWindow.xaml', [IO.FileMode]::Create, [IO.FileAccess]::Write)
try {
    $proc.StandardOutput.BaseStream.CopyTo($stream)
}
finally {
    $stream.Dispose()
}
$proc.WaitForExit()
if ($proc.ExitCode -ne 0) { throw "git cat-file falhou com codigo $($proc.ExitCode)." }

$path = 'MainWindow.xaml'
$text = [IO.File]::ReadAllText($path)

$column = '                                        <ColumnDefinition Width="135"/>  <!-- Botão Importar -->'
if (($text.Split($column).Count - 1) -ne 1) { throw 'Coluna Importar não encontrada de forma única.' }
$columnReplacement = $column + "`r`n" +
    '                                        <ColumnDefinition Width="8"/>    <!-- Espaçamento -->' + "`r`n" +
    '                                        <ColumnDefinition Width="155"/>  <!-- Conferir Pagamentos -->'
$text = $text.Replace($column, $columnReplacement)

$button = @'

                                    <!-- Botão Conferir Pagamentos -->
                                    <Button x:Name="BtnConferirPagamentos"
                                            Grid.Column="14"
                                            Content="Conferir Pagamentos"
                                            Height="38"
                                            Background="#2D6D50"
                                            Foreground="White"
                                            BorderThickness="0"
                                            FontWeight="SemiBold"
                                            FontSize="13"
                                            Cursor="Hand"
                                            Click="BtnConferirPagamentos_Click">
                                        <Button.Style>
                                            <Style TargetType="Button">
                                                <Setter Property="Background" Value="#2D6D50"/>
                                                <Setter Property="Template">
                                                    <Setter.Value>
                                                        <ControlTemplate TargetType="Button">
                                                            <Border Background="{TemplateBinding Background}"
                                                                   CornerRadius="6"
                                                                   BorderThickness="0">
                                                                <ContentPresenter HorizontalAlignment="Center"
                                                                                 VerticalAlignment="Center"/>
                                                            </Border>
                                                        </ControlTemplate>
                                                    </Setter.Value>
                                                </Setter>
                                                <Style.Triggers>
                                                    <Trigger Property="IsMouseOver" Value="True">
                                                        <Setter Property="Background" Value="#388563"/>
                                                    </Trigger>
                                                </Style.Triggers>
                                            </Style>
                                        </Button.Style>
                                    </Button>
'@
$button = $button.Replace("`r`n", "`n").Replace("`n", "`r`n")

$pattern = '(?s)(<!-- Botão Movimentar Registros -->.*?</Button>)(\r\n\s*</Grid>\r\n\s*<!-- ============================================ -->\r\n\s*<!-- LISTA DE PREVISÕES DE PAGAMENTO -->)'
$regex = [Text.RegularExpressions.Regex]::new($pattern)
if ($regex.Matches($text).Count -ne 1) { throw 'Ponto de inserção do botão não encontrado de forma única.' }
$text = $regex.Replace($text, ('$1' + $button + '$2'), 1)

[IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
Write-Host 'MainWindow reconstruído a partir do blob exato da main.'
