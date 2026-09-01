using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TaskbarMonitor
{
    public sealed class SettingsForm : Form
    {
        private readonly AppHost host;
        private readonly AppSettings working;
        private readonly DataGridView metricGrid;
        private readonly MetricBarControl preview;
        private readonly NumericUpDown intervalInput;
        private readonly NumericUpDown historyInput;
        private readonly NumericUpDown widthInput;
        private readonly NumericUpDown offsetInput;
        private readonly NumericUpDown insideItemWidthInput;
        private readonly NumericUpDown insideHeightInput;
        private readonly NumericUpDown opacityInput;
        private readonly NumericUpDown fontInput;
        private readonly ComboBox positionInput;
        private readonly ComboBox fullscreenInput;
        private readonly CheckBox autoFitInput;
        private readonly CheckBox pauseHiddenInput;
        private readonly CheckBox startupInput;
        private readonly CheckBox showSettingsInput;
        private readonly CheckBox seamlessInput;
        private readonly ToolTip helpTip;
        private MetricSnapshot lastSnapshot;
        private MetricHistory lastHistory;
        private bool loading;

        public SettingsForm(AppHost appHost, AppSettings settings)
        {
            host = appHost;
            working = settings;
            lastSnapshot = appHost.Snapshot;
            lastHistory = appHost.History;
            Text = "Taskbar Monitor 설정";
            Icon = IconFactory.CreateGraphIcon(Color.FromArgb(0, 183, 195));
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(800, 920);
            MinimumSize = new Size(760, 850);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9.0f);
            BackColor = Color.FromArgb(245, 245, 245);
            helpTip = new ToolTip();
            helpTip.ToolTipTitle = "설정 설명";
            helpTip.ToolTipIcon = ToolTipIcon.Info;
            helpTip.InitialDelay = 300;
            helpTip.ReshowDelay = 100;
            helpTip.AutoPopDelay = 12000;
            helpTip.ShowAlways = true;

            Label title = new Label();
            title.Text = "표시할 항목과 그래프를 선택하세요";
            title.Font = new Font("Segoe UI", 15.0f, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(18, 15);
            Controls.Add(title);

            Label explanation = new Label();
            explanation.Text = "항목별로 숫자와 그래프를 따로 켜고 끌 수 있습니다. 각 설정 위에 마우스를 올리면 상세 설명이 표시됩니다.";
            explanation.AutoSize = true;
            explanation.ForeColor = Color.DimGray;
            explanation.Location = new Point(20, 46);
            Controls.Add(explanation);

            metricGrid = CreateMetricGrid();
            metricGrid.Location = new Point(18, 76);
            metricGrid.Size = new Size(748, 232);
            metricGrid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(metricGrid);

            FlowLayoutPanel metricButtons = new FlowLayoutPanel();
            metricButtons.Location = new Point(18, 315);
            metricButtons.Size = new Size(748, 34);
            metricButtons.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            metricButtons.WrapContents = false;
            metricButtons.Controls.Add(NewButton("전체 표시", delegate { SetAllEnabled(true); }));
            metricButtons.Controls.Add(NewButton("모두 해제", delegate { SetAllEnabled(false); }));
            metricButtons.Controls.Add(NewButton("그래프 전체", delegate { SetAllGraphs(true); }));
            metricButtons.Controls.Add(NewButton("그래프 해제", delegate { SetAllGraphs(false); }));
            metricButtons.Controls.Add(NewButton("▲ 위로", delegate { MoveSelected(-1); }));
            metricButtons.Controls.Add(NewButton("▼ 아래로", delegate { MoveSelected(1); }));
            Controls.Add(metricButtons);

            GroupBox previewGroup = new GroupBox();
            previewGroup.Text = "실시간 미리 보기";
            previewGroup.Location = new Point(18, 354);
            previewGroup.Size = new Size(748, 82);
            previewGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            preview = new MetricBarControl();
            preview.Location = new Point(10, 21);
            preview.Size = new Size(726, 48);
            preview.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            previewGroup.Controls.Add(preview);
            Controls.Add(previewGroup);

            GroupBox globalGroup = new GroupBox();
            globalGroup.Text = "크기·동작·성능";
            globalGroup.Location = new Point(18, 446);
            globalGroup.Size = new Size(748, 235);
            globalGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(globalGroup);

            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.Padding = new Padding(10, 8, 10, 8);
            table.ColumnCount = 4;
            table.RowCount = 6;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29));
            globalGroup.Controls.Add(table);

            intervalInput = NewNumber(200, 10000, working.UpdateIntervalMs, 100, 0);
            historyInput = NewNumber(10, 600, working.HistorySeconds, 10, 0);
            widthInput = NewNumber(160, 1200, working.MaxWidth, 10, 0);
            offsetInput = NewNumber(0, 1200, working.TaskbarOffset, 5, 0);
            insideItemWidthInput = NewNumber(48, 180, working.InsideItemWidth, 2, 0);
            insideHeightInput = NewNumber(20, 48, working.InsideHeight, 1, 0);
            opacityInput = NewNumber(25, 100, working.OpacityPercent, 1, 0);
            fontInput = NewNumber(7, 18, (decimal)working.FontSize, 0.5m, 1);
            positionInput = NewCombo(new string[] { "작업 표시줄 안쪽", "작업 표시줄 위" }, working.PositionMode == "Above" ? 1 : 0);
            int fullscreenIndex = working.FullscreenMode == "Show" ? 1 : (working.FullscreenMode == "ClickThrough" ? 2 : 0);
            fullscreenInput = NewCombo(new string[] { "전체화면에서 숨기기", "항상 표시", "표시 + 클릭 통과" }, fullscreenIndex);

            AddSettingRow(table, 0, "갱신 간격 (ms)", intervalInput, "그래프 기록 (초)", historyInput);
            AddSettingRow(table, 1, "최대 너비 (px)", widthInput, "왼쪽 여백 (px)", offsetInput);
            AddSettingRow(table, 2, "투명도 (%)", opacityInput, "글꼴 크기", fontInput);
            AddSettingRow(table, 3, "표시 위치", positionInput, "전체화면 동작", fullscreenInput);
            AddSettingRow(table, 4, "내부 항목 폭 (px)", insideItemWidthInput, "내부 높이 (px)", insideHeightInput);

            FlowLayoutPanel checks = new FlowLayoutPanel();
            checks.Dock = DockStyle.Fill;
            checks.WrapContents = true;
            checks.AutoSize = true;
            autoFitInput = NewCheckBox("공간에 자동 맞춤", working.AutoFit);
            pauseHiddenInput = NewCheckBox("숨김 시 5초 절전 갱신", working.PauseWhenHidden);
            startupInput = NewCheckBox("Windows 시작 시 자동 실행", working.StartWithWindows);
            showSettingsInput = NewCheckBox("직접 실행 시 설정 먼저 표시", working.ShowSettingsOnManualLaunch);
            seamlessInput = NewCheckBox("작업 표시줄 무배경 결합", working.InsideStyle == "Seamless");
            checks.Controls.Add(autoFitInput);
            checks.Controls.Add(pauseHiddenInput);
            checks.Controls.Add(startupInput);
            checks.Controls.Add(showSettingsInput);
            checks.Controls.Add(seamlessInput);
            table.Controls.Add(checks, 0, 5);
            table.SetColumnSpan(checks, 4);

            ConfigureHelpText();

            GroupBox helpGroup = new GroupBox();
            helpGroup.Text = "설정 도움말";
            helpGroup.Location = new Point(18, 692);
            helpGroup.Size = new Size(748, 112);
            helpGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Label helpText = new Label();
            helpText.Dock = DockStyle.Fill;
            helpText.Padding = new Padding(10, 5, 10, 5);
            helpText.TextAlign = ContentAlignment.MiddleLeft;
            helpText.Text =
                "권장값은 갱신 1000ms, 그래프 기록 60초입니다. 갱신 값을 낮추면 더 빠르게 반응하지만 CPU 사용량이 늘 수 있습니다.\r\n" +
                "최대 너비·왼쪽 여백은 배치 범위, 내부 항목 폭·높이는 작업 표시줄 안쪽 칸 크기를 조절합니다.\r\n" +
                "‘표시 + 클릭 통과’는 전체화면 위에 보이되 마우스 입력을 뒤 앱으로 넘깁니다. 위 옵션에 마우스를 올리면 더 자세히 볼 수 있습니다.";
            helpGroup.Controls.Add(helpText);
            Controls.Add(helpGroup);

            FlowLayoutPanel bottom = new FlowLayoutPanel();
            bottom.FlowDirection = FlowDirection.RightToLeft;
            bottom.WrapContents = false;
            bottom.Location = new Point(18, 827);
            bottom.Size = new Size(748, 43);
            bottom.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Button startButton = NewButton("저장하고 표시 시작", SaveAndStart);
            startButton.AutoSize = true;
            startButton.Height = 32;
            Button applyButton = NewButton("적용", ApplyOnly);
            applyButton.Height = 32;
            Button closeButton = NewButton("닫기", delegate { Close(); });
            closeButton.Height = 32;
            bottom.Controls.Add(startButton);
            bottom.Controls.Add(applyButton);
            bottom.Controls.Add(closeButton);
            Controls.Add(bottom);
            AcceptButton = startButton;

            LoadMetricRows();
            WirePreviewEvents();
            RefreshPreview();
        }

        private DataGridView CreateMetricGrid()
        {
            DataGridView grid = new DataGridView();
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.ShowCellToolTips = true;

            DataGridViewCheckBoxColumn enabled = new DataGridViewCheckBoxColumn();
            enabled.Name = "Enabled";
            enabled.HeaderText = "표시";
            enabled.ToolTipText = "이 항목 전체를 위젯에 표시하거나 숨깁니다.";
            enabled.FillWeight = 48;
            grid.Columns.Add(enabled);

            DataGridViewTextBoxColumn name = new DataGridViewTextBoxColumn();
            name.Name = "Name";
            name.HeaderText = "항목";
            name.ToolTipText = "측정 대상입니다. CPU, 메모리, 디스크, 네트워크, GPU를 지원합니다.";
            name.ReadOnly = true;
            name.FillWeight = 88;
            grid.Columns.Add(name);

            DataGridViewTextBoxColumn label = new DataGridViewTextBoxColumn();
            label.Name = "Label";
            label.HeaderText = "표시 이름";
            label.ToolTipText = "위젯에 보일 짧은 이름입니다. 셀을 클릭해 직접 바꿀 수 있습니다.";
            label.FillWeight = 78;
            grid.Columns.Add(label);

            DataGridViewCheckBoxColumn value = new DataGridViewCheckBoxColumn();
            value.Name = "Value";
            value.HeaderText = "숫자";
            value.ToolTipText = "현재 사용률이나 전송 속도를 숫자로 표시합니다.";
            value.FillWeight = 48;
            grid.Columns.Add(value);

            DataGridViewCheckBoxColumn graph = new DataGridViewCheckBoxColumn();
            graph.Name = "Graph";
            graph.HeaderText = "그래프";
            graph.ToolTipText = "설정한 기록 시간만큼의 변화 추이를 미니 그래프로 표시합니다.";
            graph.FillWeight = 55;
            grid.Columns.Add(graph);

            DataGridViewComboBoxColumn style = new DataGridViewComboBoxColumn();
            style.Name = "Style";
            style.HeaderText = "그래프 형태";
            style.ToolTipText = "선은 가장 가볍고, 채움은 변화량이 잘 보이며, 막대는 순간값 비교에 좋습니다.";
            style.Items.AddRange("선", "채움", "막대");
            style.FillWeight = 85;
            grid.Columns.Add(style);

            DataGridViewButtonColumn color = new DataGridViewButtonColumn();
            color.Name = "Color";
            color.HeaderText = "색상";
            color.ToolTipText = "항목 이름과 그래프에 사용할 색상을 선택합니다.";
            color.Text = "선택";
            color.UseColumnTextForButtonValue = true;
            color.FillWeight = 58;
            grid.Columns.Add(color);

            grid.CellContentClick += MetricGridCellContentClick;
            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            return grid;
        }

        private void ConfigureHelpText()
        {
            helpTip.SetToolTip(metricGrid, "표시 이름은 직접 수정할 수 있습니다. 숫자와 그래프는 서로 독립적으로 켜고 끌 수 있습니다.");
            helpTip.SetToolTip(intervalInput, "값을 다시 읽는 주기입니다. 1000ms를 권장합니다. 200~500ms는 더 부드럽지만 CPU 사용량이 늘 수 있습니다.");
            helpTip.SetToolTip(historyInput, "미니 그래프가 기억하는 과거 시간입니다. 60초를 권장하며, 길게 잡을수록 메모리를 조금 더 사용합니다.");
            helpTip.SetToolTip(widthInput, "위젯이 차지할 수 있는 최대 가로 폭입니다. 항목이 잘리면 늘리고 작업표시줄이 좁으면 줄이세요.");
            helpTip.SetToolTip(offsetInput, "작업표시줄 왼쪽 끝에서 위젯이 시작할 위치입니다. 날씨 버튼과 시작 버튼 사이 배치를 미세 조정할 때 사용합니다.");
            helpTip.SetToolTip(opacityInput, "패널 배경의 불투명도입니다. 작업 표시줄 위 모드에서 효과가 크며, 무배경 결합 모드에서는 영향이 적습니다.");
            helpTip.SetToolTip(fontInput, "항목 이름과 숫자의 글자 크기입니다. 칸이 좁을 때는 8~9 정도가 보기 좋습니다.");
            helpTip.SetToolTip(positionInput, "안쪽은 위젯을 실제 작업표시줄에 결합하고, 위쪽은 작업표시줄 바로 위에 독립 창으로 표시합니다.");
            helpTip.SetToolTip(fullscreenInput, "숨기기: 전체화면에서 감춤 / 항상 표시: 위에 유지 / 클릭 통과: 보이지만 마우스 입력은 전체화면 앱으로 전달합니다.");
            helpTip.SetToolTip(insideItemWidthInput, "작업표시줄 안쪽 모드에서 CPU·RAM 등 항목 하나가 차지할 기준 폭입니다. 폭이 작으면 이름이 짧게 표시됩니다.");
            helpTip.SetToolTip(insideHeightInput, "작업표시줄 안쪽 위젯의 높이입니다. 기본 28px이며 작업표시줄 높이를 넘지 않도록 자동 제한됩니다.");
            helpTip.SetToolTip(autoFitInput, "날씨 버튼과 시작 버튼 사이의 실제 빈 공간에 맞춰 항목 폭을 자동으로 줄입니다.");
            helpTip.SetToolTip(pauseHiddenInput, "위젯이 보이지 않을 때 갱신 주기를 5초로 늦춰 CPU 사용량을 줄입니다.");
            helpTip.SetToolTip(startupInput, "Windows 로그인 후 저장된 설정으로 위젯을 자동 실행합니다.");
            helpTip.SetToolTip(showSettingsInput, "EXE를 직접 실행했을 때 위젯보다 설정창을 먼저 엽니다. Windows 자동 시작에는 적용되지 않습니다.");
            helpTip.SetToolTip(seamlessInput, "패널 배경과 테두리를 투명 처리해 작업표시줄 글자·그래프만 보이게 합니다.");
        }

        private void LoadMetricRows()
        {
            loading = true;
            metricGrid.Rows.Clear();
            foreach (MetricOption option in working.Metrics.OrderBy(delegate(MetricOption m) { return m.Order; }))
            {
                int index = metricGrid.Rows.Add(option.Enabled, option.DisplayName, option.Label, option.ShowValue,
                    option.ShowGraph, StyleDisplay(option.GraphStyle), "선택");
                DataGridViewRow row = metricGrid.Rows[index];
                row.Tag = option;
                row.Cells["Color"].Style.BackColor = option.Color;
                row.Cells["Color"].Style.SelectionBackColor = option.Color;
                row.Cells["Color"].Style.ForeColor = ContrastColor(option.Color);
                row.Cells["Color"].Style.SelectionForeColor = ContrastColor(option.Color);
            }
            loading = false;
        }

        private void MetricGridCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || metricGrid.Columns[e.ColumnIndex].Name != "Color") return;
            MetricOption option = (MetricOption)metricGrid.Rows[e.RowIndex].Tag;
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.Color = option.Color;
                dialog.FullOpen = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    option.ColorArgb = dialog.Color.ToArgb();
                    metricGrid.Rows[e.RowIndex].Cells["Color"].Style.BackColor = dialog.Color;
                    metricGrid.Rows[e.RowIndex].Cells["Color"].Style.SelectionBackColor = dialog.Color;
                    metricGrid.Rows[e.RowIndex].Cells["Color"].Style.ForeColor = ContrastColor(dialog.Color);
                    metricGrid.Rows[e.RowIndex].Cells["Color"].Style.SelectionForeColor = ContrastColor(dialog.Color);
                    RefreshPreview();
                }
            }
        }

        private static Color ContrastColor(Color color)
        {
            double luminance = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
            return luminance > 150 ? Color.Black : Color.White;
        }

        private void WirePreviewEvents()
        {
            metricGrid.CellValueChanged += delegate { if (!loading) RefreshPreview(); };
            metricGrid.CellEndEdit += delegate { if (!loading) RefreshPreview(); };
            foreach (Control control in new Control[] { intervalInput, historyInput, widthInput, offsetInput, insideItemWidthInput, insideHeightInput, opacityInput, fontInput })
                ((NumericUpDown)control).ValueChanged += delegate { RefreshPreview(); };
            positionInput.SelectedIndexChanged += delegate { RefreshPreview(); };
            fullscreenInput.SelectedIndexChanged += delegate { RefreshPreview(); };
            autoFitInput.CheckedChanged += delegate { RefreshPreview(); };
        }

        private void ReadControlsToWorking()
        {
            foreach (DataGridViewRow row in metricGrid.Rows)
            {
                MetricOption option = (MetricOption)row.Tag;
                option.Enabled = Convert.ToBoolean(row.Cells["Enabled"].Value ?? false);
                option.Label = Convert.ToString(row.Cells["Label"].Value) ?? option.DisplayName;
                option.ShowValue = Convert.ToBoolean(row.Cells["Value"].Value ?? false);
                option.ShowGraph = Convert.ToBoolean(row.Cells["Graph"].Value ?? false);
                option.GraphStyle = StyleValue(Convert.ToString(row.Cells["Style"].Value));
                option.Order = row.Index;
            }
            working.UpdateIntervalMs = (int)intervalInput.Value;
            working.HistorySeconds = (int)historyInput.Value;
            working.MaxWidth = (int)widthInput.Value;
            working.TaskbarOffset = (int)offsetInput.Value;
            working.InsideItemWidth = (int)insideItemWidthInput.Value;
            working.InsideHeight = (int)insideHeightInput.Value;
            working.OpacityPercent = (int)opacityInput.Value;
            working.FontSize = (float)fontInput.Value;
            working.PositionMode = positionInput.SelectedIndex == 1 ? "Above" : "Inside";
            working.FullscreenMode = fullscreenInput.SelectedIndex == 1 ? "Show" : (fullscreenInput.SelectedIndex == 2 ? "ClickThrough" : "Hide");
            working.AutoFit = autoFitInput.Checked;
            working.PauseWhenHidden = pauseHiddenInput.Checked;
            working.StartWithWindows = startupInput.Checked;
            working.ShowSettingsOnManualLaunch = showSettingsInput.Checked;
            working.InsideStyle = seamlessInput.Checked ? "Seamless" : "Panel";
        }

        private void RefreshPreview()
        {
            if (loading) return;
            try
            {
                ReadControlsToWorking();
                bool inside = working.PositionMode == "Inside";
                preview.SetIntegratedStyle(inside, Color.FromArgb(31, 31, 31));
                preview.Configure(working, lastSnapshot, lastHistory);
                preview.Width = Math.Min(preview.Parent.ClientSize.Width - 20, preview.GetPreferredWidth());
                preview.Height = inside ? working.InsideHeight : 48;
            }
            catch
            {
            }
        }

        public void UpdatePreview(MetricSnapshot snapshot, MetricHistory history)
        {
            lastSnapshot = snapshot;
            lastHistory = history;
            bool inside = working.PositionMode == "Inside";
            preview.SetIntegratedStyle(inside, Color.FromArgb(31, 31, 31));
            preview.Configure(working, snapshot, history);
            preview.Width = Math.Min(preview.Parent.ClientSize.Width - 20, preview.GetPreferredWidth());
            preview.Height = inside ? working.InsideHeight : 48;
        }

        private void SaveAndStart(object sender, EventArgs e)
        {
            ReadControlsToWorking();
            if (!working.Metrics.Any(delegate(MetricOption m) { return m.Enabled; }))
            {
                MessageBox.Show(this, "표시할 항목을 하나 이상 선택하세요.", "Taskbar Monitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            host.ApplySettings(working, true);
            Close();
        }

        private void ApplyOnly(object sender, EventArgs e)
        {
            ReadControlsToWorking();
            host.ApplySettings(working, false);
            RefreshPreview();
        }

        private void SetAllEnabled(bool value)
        {
            foreach (DataGridViewRow row in metricGrid.Rows) row.Cells["Enabled"].Value = value;
            RefreshPreview();
        }

        private void SetAllGraphs(bool value)
        {
            foreach (DataGridViewRow row in metricGrid.Rows) row.Cells["Graph"].Value = value;
            RefreshPreview();
        }

        private void MoveSelected(int direction)
        {
            if (metricGrid.SelectedRows.Count == 0) return;
            ReadControlsToWorking();
            int current = metricGrid.SelectedRows[0].Index;
            int target = current + direction;
            if (target < 0 || target >= metricGrid.Rows.Count) return;
            List<MetricOption> ordered = working.Metrics.OrderBy(delegate(MetricOption m) { return m.Order; }).ToList();
            MetricOption temporary = ordered[current];
            ordered[current] = ordered[target];
            ordered[target] = temporary;
            for (int index = 0; index < ordered.Count; index++) ordered[index].Order = index;
            working.Metrics = ordered;
            LoadMetricRows();
            metricGrid.Rows[target].Selected = true;
            RefreshPreview();
        }

        private static Button NewButton(string text, EventHandler handler)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.Height = 28;
            button.Click += handler;
            return button;
        }

        private static NumericUpDown NewNumber(decimal minimum, decimal maximum, decimal value, decimal increment, int decimals)
        {
            NumericUpDown input = new NumericUpDown();
            input.Minimum = minimum;
            input.Maximum = maximum;
            input.Value = Math.Max(minimum, Math.Min(maximum, value));
            input.Increment = increment;
            input.DecimalPlaces = decimals;
            input.Dock = DockStyle.Fill;
            return input;
        }

        private static ComboBox NewCombo(string[] items, int selectedIndex)
        {
            ComboBox combo = new ComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Items.AddRange(items);
            combo.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, selectedIndex));
            combo.Dock = DockStyle.Fill;
            return combo;
        }

        private static CheckBox NewCheckBox(string text, bool value)
        {
            CheckBox check = new CheckBox();
            check.Text = text;
            check.Checked = value;
            check.AutoSize = true;
            check.Margin = new Padding(3, 3, 14, 3);
            return check;
        }

        private static void AddSettingRow(TableLayoutPanel table, int row, string leftLabel, Control leftControl, string rightLabel, Control rightControl)
        {
            Label left = new Label();
            left.Text = leftLabel;
            left.Dock = DockStyle.Fill;
            left.TextAlign = ContentAlignment.MiddleLeft;
            Label right = new Label();
            right.Text = rightLabel;
            right.Dock = DockStyle.Fill;
            right.TextAlign = ContentAlignment.MiddleLeft;
            table.Controls.Add(left, 0, row);
            table.Controls.Add(leftControl, 1, row);
            table.Controls.Add(right, 2, row);
            table.Controls.Add(rightControl, 3, row);
        }

        private static string StyleDisplay(string value)
        {
            if (value == "Fill") return "채움";
            if (value == "Bars") return "막대";
            return "선";
        }

        private static string StyleValue(string value)
        {
            if (value == "채움") return "Fill";
            if (value == "막대") return "Bars";
            return "Line";
        }
    }
}
