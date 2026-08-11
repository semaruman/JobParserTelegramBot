using JobParserTelegramBot.Models;
using JobParserTelegramBot.Services.Telegram;

namespace JobParserTelegramBot.UI;

public sealed class MainForm : Form
{
    private readonly IChatManagementService _chatManagement;
    private readonly ITelegramSessionService _session;
    private readonly IVacancyHistoryScanService _historyScan;

    private readonly ListView _listView;
    private readonly TextBox _inputBox;
    private readonly Button _addButton;
    private readonly Button _removeButton;
    private readonly Button _refreshButton;
    private readonly Button _scanDayButton;
    private readonly Button _toggleButton;
    private readonly Label _statusLabel;
    private readonly System.Windows.Forms.Timer _statusTimer;
    private CancellationTokenSource? _scanCts;

    public MainForm(
        IChatManagementService chatManagement,
        ITelegramSessionService session,
        IVacancyHistoryScanService historyScan)
    {
        _chatManagement = chatManagement;
        _session = session;
        _historyScan = historyScan;

        Text = "Job Parser — каналы с вакансиями";
        Width = 720;
        Height = 480;
        MinimumSize = new Size(560, 360);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10F);

        _statusLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            Text = "Запуск…"
        };

        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(8)
        };

        _inputBox = new TextBox
        {
            PlaceholderText = "@channel или chat id",
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            Width = 360,
            Location = new Point(8, 10)
        };

        _addButton = new Button
        {
            Text = "Добавить",
            Width = 100,
            Location = new Point(380, 8),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _addButton.Click += async (_, _) => await AddChatAsync();

        _removeButton = new Button
        {
            Text = "Удалить",
            Width = 100,
            Location = new Point(490, 8),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _removeButton.Click += async (_, _) => await RemoveSelectedAsync();

        _toggleButton = new Button
        {
            Text = "Вкл/Выкл",
            Width = 90,
            Location = new Point(600, 8),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _toggleButton.Click += async (_, _) => await ToggleSelectedAsync();

        bottomPanel.Controls.Add(_inputBox);
        bottomPanel.Controls.Add(_addButton);
        bottomPanel.Controls.Add(_removeButton);
        bottomPanel.Controls.Add(_toggleButton);
        bottomPanel.Resize += (_, _) => LayoutBottom(bottomPanel);

        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(8, 6, 8, 6)
        };

        _refreshButton = new Button
        {
            Text = "Обновить",
            Width = 100,
            Dock = DockStyle.Right
        };
        _refreshButton.Click += async (_, _) => await ReloadAsync();

        _scanDayButton = new Button
        {
            Text = "Загрузить за день",
            Width = 150,
            Dock = DockStyle.Right,
            Margin = new Padding(0, 0, 8, 0)
        };
        _scanDayButton.Click += async (_, _) => await ScanLastDayAsync();

        var hint = new Label
        {
            Text = "Бот слушает каналы онлайн. «Загрузить за день» — догнать посты за 24 часа.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        topPanel.Controls.Add(hint);
        topPanel.Controls.Add(_scanDayButton);
        topPanel.Controls.Add(_refreshButton);

        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false
        };
        _listView.Columns.Add("Статус", 70);
        _listView.Columns.Add("Название", 260);
        _listView.Columns.Add("Username", 160);
        _listView.Columns.Add("Id", 160);

        Controls.Add(_listView);
        Controls.Add(bottomPanel);
        Controls.Add(topPanel);
        Controls.Add(_statusLabel);

        _statusTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        _statusTimer.Tick += (_, _) => UpdateSessionStatus();

        Load += async (_, _) =>
        {
            _statusTimer.Start();
            LayoutBottom(bottomPanel);
            await ReloadAsync();
        };

        FormClosing += (_, e) =>
        {
            _statusTimer.Stop();
            _scanCts?.Cancel();
            SetStatus("Остановка бота…");
        };

        AcceptButton = _addButton;
    }

    private void LayoutBottom(Panel bottomPanel)
    {
        _toggleButton.Left = bottomPanel.ClientSize.Width - _toggleButton.Width - 8;
        _removeButton.Left = _toggleButton.Left - _removeButton.Width - 8;
        _addButton.Left = _removeButton.Left - _addButton.Width - 8;
        _inputBox.Width = Math.Max(120, _addButton.Left - 16);
    }

    private void UpdateSessionStatus()
    {
        if (_session.Self is null)
        {
            SetStatus("Telegram: подключение / логин…");
            return;
        }

        var name = string.IsNullOrWhiteSpace(_session.Self.username)
            ? $"{_session.Self.first_name} {_session.Self.last_name}".Trim()
            : "@" + _session.Self.username;
        SetStatus($"Telegram: онлайн как {name}  |  мониторинг активен");
    }

    private async Task ReloadAsync()
    {
        try
        {
            SetBusy(true);
            var chats = await _chatManagement.ListAsync();
            _listView.BeginUpdate();
            _listView.Items.Clear();
            foreach (var chat in chats.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase))
            {
                var item = new ListViewItem(chat.Enabled ? "Вкл" : "Выкл")
                {
                    Tag = chat,
                    ForeColor = chat.Enabled ? SystemColors.WindowText : SystemColors.GrayText
                };
                item.SubItems.Add(chat.Title);
                item.SubItems.Add(string.IsNullOrEmpty(chat.Username) ? "—" : "@" + chat.Username);
                item.SubItems.Add(chat.Id.ToString());
                _listView.Items.Add(item);
            }

            _listView.EndUpdate();
            SetStatus($"Загружено каналов: {chats.Count}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка загрузки", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            UpdateSessionStatus();
        }
    }

    private async Task AddChatAsync()
    {
        var raw = _inputBox.Text.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            MessageBox.Show(this, "Введи @username или id чата.", "Добавление", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_session.Self is null)
        {
            MessageBox.Show(this, "Подожди, пока Telegram залогинится (смотри статус внизу).", "Telegram", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            SetBusy(true);
            SetStatus($"Добавляю {raw}…");
            var chat = await _chatManagement.AddAsync(raw);
            _inputBox.Clear();
            await ReloadAsync();
            SetStatus($"Добавлено: {chat.Title}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Не удалось добавить", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Ошибка добавления");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RemoveSelectedAsync()
    {
        if (_listView.SelectedItems.Count == 0 || _listView.SelectedItems[0].Tag is not ChatSource chat)
        {
            MessageBox.Show(this, "Выбери канал в списке.", "Удаление", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Удалить «{chat.Title}» из мониторинга?",
            "Подтверждение",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            SetBusy(true);
            await _chatManagement.RemoveByIdAsync(chat.Id);
            await ReloadAsync();
            SetStatus($"Удалено: {chat.Title}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка удаления", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ToggleSelectedAsync()
    {
        if (_listView.SelectedItems.Count == 0 || _listView.SelectedItems[0].Tag is not ChatSource chat)
        {
            MessageBox.Show(this, "Выбери канал в списке.", "Вкл/Выкл", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            SetBusy(true);
            await _chatManagement.SetEnabledAsync(chat.Id, !chat.Enabled);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ScanLastDayAsync()
    {
        if (_session.Self is null)
        {
            MessageBox.Show(this, "Подожди, пока Telegram залогинится.", "Telegram", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            "Просканировать все включённые каналы за последние 24 часа?\nПодходящие вакансии придут в Избранное.\nЭто может занять несколько минут.",
            "Загрузка за день",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        _scanCts = new CancellationTokenSource();
        var progress = new Progress<string>(msg =>
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(() => SetStatus(msg));
            }
        });

        try
        {
            SetBusy(true);
            SetStatus("Сканирование за последние 24 часа…");
            var result = await Task.Run(
                () => _historyScan.ScanLastDayAsync(progress, _scanCts.Token),
                _scanCts.Token);

            MessageBox.Show(
                this,
                $"Готово.\nКаналов: {result.ChatsScanned}\nСообщений проверено: {result.MessagesChecked}\nКарточек отправлено: {result.CardsSent}\nОшибок: {result.Errors}",
                "Загрузка за день",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            SetStatus($"За день: отправлено {result.CardsSent} из {result.MessagesChecked} сообщений");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Сканирование отменено");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка сканирования", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Ошибка сканирования");
        }
        finally
        {
            _scanCts?.Dispose();
            _scanCts = null;
            SetBusy(false);
            UpdateSessionStatus();
        }
    }

    private void SetBusy(bool busy)
    {
        _addButton.Enabled = !busy;
        _removeButton.Enabled = !busy;
        _refreshButton.Enabled = !busy;
        _scanDayButton.Enabled = !busy;
        _toggleButton.Enabled = !busy;
        _inputBox.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void SetStatus(string text) => _statusLabel.Text = text;
}
