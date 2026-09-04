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
        private AppSettings working;
        private readonly DataGridView metricGrid;
        private readonly MetricBarControl preview;
        private readonly NumericUpDown intervalInput;
        private readonly NumericUpDown historyInput;
        private readonly NumericUpDown widthInput;
        private readonly NumericUpDown offsetInput;
        private readonly NumericUpDown insideItemWidthInput;
        private readonly NumericUpDown insideHeightInput;
        private readonly NumericUpDown popupWidthInput;
        private readonly NumericUpDown popupHeightInput;
        private readonly NumericUpDown opacityInput;
        private readonly NumericUpDown fontInput;
        private readonly ComboBox positionInput;
        private readonly ComboBox fullscreenInput;
        private readonly ComboBox floatingOrderInput;
        private readonly ComboBox hiddenMeasurementInput;
        private readonly CheckBox autoFitInput;
        private readonly CheckBox startupInput;
        private readonly CheckBox showSettingsInput;
        private readonly CheckBox seamlessInput;
        private readonly CheckBox widgetInteractionInput;
        private readonly CheckBox overflowPagingInput;
        private readonly CheckBox popupPinnedInput;
        private readonly CheckBox popupShowOnStartupInput;
        private readonly CheckedListBox diskList;
        private readonly ToolTip helpTip;
        private readonly Label applyStatus;
        private MetricSnapshot lastSnapshot;
        private MetricHistory lastHistory;
        private bool loading;
        private bool dropDownOpen;

        public SettingsForm(AppHost appHost, AppSettings settings)
        {
            host = appHost;
            working = settings;
            lastSnapshot = appHost.Snapshot;
            lastHistory = appHost.History;
            Text = "Taskbar Monitor 설정";
            Icon = IconFactory.CreateGraphIcon(Color.FromArgb(0, 183, 195));
            StartPosition = FormStartPosition.CenterScreen;
            Rectangle workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Max(680, Math.Min(800, workingArea.Width - 32)),
                Math.Max(620, Math.Min(1040, workingArea.Height - 32)));
            MinimumSize = new Size(680, 600);
            AutoScroll = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9.0f);
            BackColor = Color.FromArgb(245, 245, 245);
            TopMost = false;

            TableLayoutPanel formLayout = new TableLayoutPanel();
            formLayout.Dock = DockStyle.Fill;
            formLayout.Margin = Padding.Empty;
            formLayout.Padding = Padding.Empty;
            formLayout.ColumnCount = 1;
            formLayout.RowCount = 2;
            formLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100.0f));
            formLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100.0f));
            formLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52.0f));
            Controls.Add(formLayout);

            Panel contentPanel = new Panel();
            contentPanel.Dock = DockStyle.Fill;
            contentPanel.Margin = Padding.Empty;
            contentPanel.AutoScroll = true;
            contentPanel.AutoScrollMinSize = new Size(0, 940);
            contentPanel.BackColor = BackColor;
            formLayout.Controls.Add(contentPanel, 0, 0);
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
            contentPanel.Controls.Add(title);

            Label explanation = new Label();
            explanation.Text = "항목별로 온도·숫자·그래프를 따로 켜고 끌 수 있습니다. 각 설정 위에 마우스를 올리면 상세 설명이 표시됩니다.";
            explanation.AutoSize = true;
            explanation.ForeColor = Color.DimGray;
            explanation.Location = new Point(20, 46);
            contentPanel.Controls.Add(explanation);

            metricGrid = CreateMetricGrid();
            metricGrid.Location = new Point(18, 76);
            metricGrid.Size = new Size(748, 232);
            metricGrid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            contentPanel.Controls.Add(metricGrid);

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
            contentPanel.Controls.Add(metricButtons);

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
            contentPanel.Controls.Add(previewGroup);

            GroupBox globalGroup = new GroupBox();
            globalGroup.Text = "크기·동작·성능";
            globalGroup.Location = new Point(18, 446);
            globalGroup.Size = new Size(748, 270);
            globalGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            contentPanel.Controls.Add(globalGroup);

            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.Padding = new Padding(10, 8, 10, 8);
            table.ColumnCount = 4;
            table.RowCount = 7;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29));
            for (int rowIndex = 0; rowIndex < 6; rowIndex++) table.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            globalGroup.Controls.Add(table);

            intervalInput = NewNumber(200, 10000, working.UpdateIntervalMs, 100, 0);
            historyInput = NewNumber(10, 600, working.HistorySeconds, 10, 0);
            widthInput = NewNumber(160, 1200, working.MaxWidth, 10, 0);
            offsetInput = NewNumber(0, 1200, working.TaskbarOffset, 5, 0);
            insideItemWidthInput = NewNumber(48, 180, working.InsideItemWidth, 2, 0);
            insideHeightInput = NewNumber(20, 48, working.InsideHeight, 1, 0);
            popupWidthInput = NewNumber(200, 1200, working.PopupWidth, 10, 0);
            popupHeightInput = NewNumber(48, 400, working.PopupHeight, 5, 0);
            opacityInput = NewNumber(25, 100, working.OpacityPercent, 1, 0);
            fontInput = NewNumber(7, 18, (decimal)working.FontSize, 0.5m, 1);
            int positionIndex = working.PositionMode == "Popup" ? 2 : (working.PositionMode == "Above" ? 1 : 0);
            positionInput = NewCombo(new string[]
            {
                "작업 표시줄 안쪽 (결합)",
                "작업 표시줄 위 (얇은 바)",
                "독립 팝업 (이동·크기 조절)"
            }, positionIndex);
            int fullscreenIndex = working.FullscreenMode == "Show" ? 1 : (working.FullscreenMode == "ClickThrough" ? 2 : 0);
            fullscreenInput = NewCombo(new string[] { "전체화면에서 숨기기", "항상 표시", "표시 + 클릭 통과" }, fullscreenIndex);
            int floatingOrderIndex = String.Equals(working.FloatingZOrder, "Top", StringComparison.OrdinalIgnoreCase) ? 1 :
                (String.Equals(working.FloatingZOrder, "Bottom", StringComparison.OrdinalIgnoreCase) ? 2 : 0);
            floatingOrderInput = NewCombo(new string[] { "일반 창 순서 (권장)", "항상 위로", "항상 뒤로" }, floatingOrderIndex);
            floatingOrderInput.Width = 170;

            AddSettingRow(table, 0, "갱신 간격 (ms)", intervalInput, "그래프 기록 (초)", historyInput);
            AddSettingRow(table, 1, "최대 너비 (px)", widthInput, "왼쪽 여백 (px)", offsetInput);
            AddSettingRow(table, 2, "투명도 (%)", opacityInput, "글꼴 크기", fontInput);
            AddSettingRow(table, 3, "표시 위치", positionInput, "전체화면 동작", fullscreenInput);
            AddSettingRow(table, 4, "내부 항목 폭 (px)", insideItemWidthInput, "내부 높이 (px)", insideHeightInput);
            AddSettingRow(table, 5, "팝업 너비 (px)", popupWidthInput, "팝업 높이 (px)", popupHeightInput);

            FlowLayoutPanel checks = new FlowLayoutPanel();
            checks.Dock = DockStyle.Fill;
            checks.WrapContents = true;
            checks.AutoSize = true;
            autoFitInput = NewCheckBox("공간에 자동 맞춤", working.AutoFit);
            int hiddenMeasurementIndex = String.Equals(working.HiddenMeasurementMode, "Throttle", StringComparison.OrdinalIgnoreCase) ? 1 :
                (String.Equals(working.HiddenMeasurementMode, "Continue", StringComparison.OrdinalIgnoreCase) ? 2 : 0);
            hiddenMeasurementInput = NewCombo(new string[]
            {
                "완전히 중지 (권장)",
                "5초 간격 절전",
                "계속 측정"
            }, hiddenMeasurementIndex);
            hiddenMeasurementInput.Width = 145;
            startupInput = NewCheckBox("Windows 시작 시 자동 실행", working.StartWithWindows);
            showSettingsInput = NewCheckBox("직접 실행 시 설정 먼저 표시", working.ShowSettingsOnManualLaunch);
            seamlessInput = NewCheckBox("작업표시줄 배경 투명", working.InsideStyle == "Seamless");
            widgetInteractionInput = NewCheckBox("위젯 전체 영역 클릭 인식", working.WidgetInteractionEnabled);
            overflowPagingInput = NewCheckBox("공간 초과 시 좌우 페이지", working.OverflowPaging);
            popupPinnedInput = NewCheckBox("현재 팝업 위치 고정", working.PopupPinned);
            popupShowOnStartupInput = NewCheckBox("앱 시작 시 팝업 표시", working.PopupShowOnStartup);
            checks.Controls.Add(autoFitInput);
            Label hiddenMeasurementLabel = new Label();
            hiddenMeasurementLabel.Text = "숨김 중 측정";
            hiddenMeasurementLabel.AutoSize = true;
            hiddenMeasurementLabel.Margin = new Padding(8, 7, 3, 3);
            checks.Controls.Add(hiddenMeasurementLabel);
            checks.Controls.Add(hiddenMeasurementInput);
            checks.Controls.Add(startupInput);
            checks.Controls.Add(showSettingsInput);
            checks.Controls.Add(seamlessInput);
            checks.Controls.Add(widgetInteractionInput);
            checks.Controls.Add(overflowPagingInput);
            checks.Controls.Add(popupPinnedInput);
            checks.Controls.Add(popupShowOnStartupInput);
            Label floatingOrderLabel = new Label();
            floatingOrderLabel.Text = "떠있는 창 순서";
            floatingOrderLabel.AutoSize = true;
            floatingOrderLabel.Margin = new Padding(8, 7, 3, 3);
            checks.Controls.Add(floatingOrderLabel);
            checks.Controls.Add(floatingOrderInput);
            table.Controls.Add(checks, 0, 6);
            table.SetColumnSpan(checks, 4);
            UpdateModeControlAvailability();

            GroupBox diskGroup = new GroupBox();
            diskGroup.Text = "표시할 디스크 드라이브";
            diskGroup.Location = new Point(18, 727);
            diskGroup.Size = new Size(748, 82);
            diskGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            diskList = new CheckedListBox();
            diskList.Dock = DockStyle.Fill;
            diskList.CheckOnClick = true;
            diskList.MultiColumn = true;
            diskList.ColumnWidth = 82;
            diskList.BorderStyle = BorderStyle.None;
            diskList.Padding = new Padding(8, 4, 8, 4);
            List<string> diskNames = AppSettings.GetAvailableDiskNames();
            foreach (string selected in working.SelectedDisks)
                if (!diskNames.Contains(selected, StringComparer.OrdinalIgnoreCase)) diskNames.Add(selected);
            foreach (string diskName in diskNames.OrderBy(delegate(string name) { return name; }))
                diskList.Items.Add(diskName, working.SelectedDisks.Contains(diskName, StringComparer.OrdinalIgnoreCase));
            diskGroup.Controls.Add(diskList);
            contentPanel.Controls.Add(diskGroup);

            ConfigureHelpText();

            GroupBox helpGroup = new GroupBox();
            helpGroup.Text = "설정 도움말";
            helpGroup.Location = new Point(18, 820);
            helpGroup.Size = new Size(748, 100);
            helpGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Label helpText = new Label();
            helpText.Dock = DockStyle.Fill;
            helpText.Padding = new Padding(10, 5, 10, 5);
            helpText.TextAlign = ContentAlignment.MiddleLeft;
            helpText.Text =
                "권장값은 갱신 1000ms, 그래프 기록 60초입니다. 갱신 값을 낮추면 더 빠르게 반응하지만 CPU 사용량이 늘 수 있습니다.\r\n" +
                "온도는 이름과 사용률 사이에 표시합니다. GPU는 NVIDIA 센서, CPU는 Windows ACPI 센서가 제공될 때 표시하며 없으면 생략합니다.\r\n" +
                "세 표시 모드는 설정과 우클릭 메뉴에서 언제든 바로 전환할 수 있습니다. 별도 상세 그래프 창은 열지 않습니다.\r\n" +
                "숨김 중 측정의 기본값은 '완전히 중지'입니다. 5초 간격과 계속 측정은 숨겨진 동안에도 기록이 필요한 경우에만 선택하세요.";
            helpGroup.Controls.Add(helpText);
            contentPanel.Controls.Add(helpGroup);

            FlowLayoutPanel bottom = new FlowLayoutPanel();
            bottom.FlowDirection = FlowDirection.RightToLeft;
            bottom.WrapContents = false;
            bottom.Dock = DockStyle.Fill;
            bottom.Margin = Padding.Empty;
            bottom.Padding = new Padding(12, 7, 12, 5);
            bottom.BackColor = Color.FromArgb(245, 245, 245);
            bottom.BorderStyle = BorderStyle.FixedSingle;
            Button startButton = NewButton("저장하고 표시 적용", SaveAndStart);
            startButton.AutoSize = true;
            startButton.Height = 32;
            Button applyButton = NewButton("적용", ApplyOnly);
            applyButton.Height = 32;
            Button closeButton = NewButton("닫기", delegate { Close(); });
            closeButton.Height = 32;
            Button resetButton = NewButton("기본 설정 복원", ResetDefaults);
            resetButton.Height = 32;
            helpTip.SetToolTip(resetButton, "설정 화면의 모든 항목을 처음 설치 상태로 되돌립니다. 적용 또는 저장 버튼을 누르기 전에는 실제 설정에 저장되지 않습니다.");
            bottom.Controls.Add(startButton);
            bottom.Controls.Add(applyButton);
            bottom.Controls.Add(closeButton);
            applyStatus = new Label();
            applyStatus.AutoSize = true;
            applyStatus.ForeColor = Color.FromArgb(0, 110, 80);
            applyStatus.Margin = new Padding(12, 9, 3, 3);
            bottom.Controls.Add(applyStatus);
            bottom.Controls.Add(resetButton);
            formLayout.Controls.Add(bottom, 0, 1);
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

            DataGridViewCheckBoxColumn temperature = new DataGridViewCheckBoxColumn();
            temperature.Name = "Temperature";
            temperature.HeaderText = "온도";
            temperature.ToolTipText = "CPU/GPU 온도를 이름과 사용률 사이에 표시합니다. 센서값을 읽을 수 없으면 자동으로 생략합니다.";
            temperature.FillWeight = 48;
            grid.Columns.Add(temperature);

            DataGridViewButtonColumn temperatureColor = new DataGridViewButtonColumn();
            temperatureColor.Name = "TemperatureColor";
            temperatureColor.HeaderText = "온도 색";
            temperatureColor.ToolTipText = "CPU/GPU 온도 글자의 색상을 선택합니다. 기본값은 주황색입니다.";
            temperatureColor.Text = "선택";
            temperatureColor.UseColumnTextForButtonValue = true;
            temperatureColor.FillWeight = 58;
            grid.Columns.Add(temperatureColor);

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

            DataGridViewComboBoxColumn format = new DataGridViewComboBoxColumn();
            format.Name = "Format";
            format.HeaderText = "값 형식";
            format.ToolTipText = "메모리는 %, 사용 GB, 사용/전체 GB 중에서 고를 수 있습니다.";
            format.Items.AddRange("기본", "%", "사용 GB", "사용/전체 GB");
            format.FillWeight = 88;
            grid.Columns.Add(format);

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
            helpTip.SetToolTip(metricGrid, "표시 이름은 직접 수정할 수 있습니다. CPU/GPU 온도, 숫자와 그래프는 서로 독립적으로 켜고 끌 수 있습니다. 온도 색도 CPU/GPU별로 선택할 수 있습니다.");
            helpTip.SetToolTip(intervalInput, "값을 다시 읽는 주기입니다. 1000ms를 권장합니다. 200~500ms는 더 부드럽지만 CPU 사용량이 늘 수 있습니다.");
            helpTip.SetToolTip(historyInput, "미니 그래프가 기억하는 과거 시간입니다. 60초를 권장하며, 길게 잡을수록 메모리를 조금 더 사용합니다.");
            helpTip.SetToolTip(widthInput, "위젯이 차지할 수 있는 최대 가로 폭입니다. 항목이 잘리면 늘리고 작업표시줄이 좁으면 줄이세요.");
            helpTip.SetToolTip(offsetInput, "작업표시줄 왼쪽 끝에서 위젯이 시작할 위치입니다. 날씨 버튼과 시작 버튼 사이 배치를 미세 조정할 때 사용합니다.");
            helpTip.SetToolTip(opacityInput, "패널 배경의 불투명도입니다. 작업 표시줄 위·팝업 모드에서 효과가 크며, 작업표시줄 배경 투명을 켠 안쪽 모드에서는 적용되지 않습니다.");
            helpTip.SetToolTip(fontInput, "항목 이름과 숫자의 글자 크기입니다. 칸이 좁을 때는 8~9 정도가 보기 좋습니다.");
            helpTip.SetToolTip(positionInput, "안쪽: Windows 11 작업표시줄의 날씨와 시작 버튼 사이 빈 공간에 맞춰 표시 / 위쪽: 작업표시줄 위의 얇은 창 / 팝업: 이동·크기 조절 가능한 독립 창입니다.");
            helpTip.SetToolTip(fullscreenInput, "숨기기: 전체화면에서 감춤 / 항상 표시: 위에 유지 / 클릭 통과: 보이지만 마우스 입력은 전체화면 앱으로 전달합니다.");
            helpTip.SetToolTip(insideItemWidthInput, "작업표시줄 안쪽 모드에서 CPU·RAM 등 항목 하나가 차지할 기준 폭입니다. 폭이 작으면 이름이 짧게 표시됩니다.");
            helpTip.SetToolTip(insideHeightInput, "작업표시줄 안쪽 위젯의 높이입니다. 기본 28px이며 작업표시줄 높이를 넘지 않도록 자동 제한됩니다.");
            helpTip.SetToolTip(popupWidthInput, "독립 팝업의 가로 크기입니다. 기본 500px이며 200~1200px 범위에서 조절할 수 있습니다.");
            helpTip.SetToolTip(popupHeightInput, "독립 팝업의 세로 크기입니다. 기본 72px이며 48~400px 범위에서 조절할 수 있습니다.");
            helpTip.SetToolTip(floatingOrderInput, "일반: 다른 창을 사용하면 자연스럽게 뒤로 감 / 항상 위: 직접 선택한 경우에만 최상단 / 항상 뒤: 다른 일반 창 뒤에 둡니다.");
            helpTip.SetToolTip(autoFitInput, "날씨 버튼과 시작 버튼 사이의 실제 빈 공간에 맞춰 항목 폭을 자동으로 줄입니다.");
            helpTip.SetToolTip(hiddenMeasurementInput, "위젯과 설정창이 모두 숨겨졌을 때만 적용됩니다. 완전히 중지(기본)는 하드웨어 측정과 그래프 기록을 쉬게 합니다. 5초 간격은 숨김 중에도 듬성듬성 기록할 때, 계속 측정은 기록을 끊지 않을 때만 사용하세요.");
            helpTip.SetToolTip(startupInput, "Windows 로그인 후 저장된 설정으로 위젯을 자동 실행합니다.");
            helpTip.SetToolTip(showSettingsInput, "EXE를 직접 실행했을 때 위젯보다 설정창을 먼저 엽니다. Windows 자동 시작에는 적용되지 않습니다.");
            helpTip.SetToolTip(seamlessInput, "작업표시줄 안쪽 모드에서만 적용됩니다. 켜면 위젯 배경을 투명하게 해 글자와 그래프만 보이고, 끄면 패널 배경·테두리·항목 구분선을 표시합니다.");
            helpTip.SetToolTip(widgetInteractionInput, "켜면 위젯 전체에서 클릭·우클릭·더블클릭과 팝업 이동을 사용할 수 있습니다. 끄면 작업 관리자 실행을 포함한 모든 위젯 입력이 차단되고 뒤 창으로 통과합니다. 다시 켤 때는 트레이 아이콘의 우클릭 메뉴를 사용하세요.");
            helpTip.SetToolTip(overflowPagingInput, "항목이 표시 공간보다 많을 때 폭을 계속 줄이지 않고 좌우 화살표로 페이지를 전환합니다. 페이지별 항목 수도 최대한 균등하게 나눕니다.");
            helpTip.SetToolTip(popupPinnedInput, "팝업을 원하는 곳으로 옮기고 크기를 맞춘 뒤 켜세요. 현재 좌표를 저장하며 이동과 크기 조절을 잠급니다. 끄면 다시 조절할 수 있습니다.");
            helpTip.SetToolTip(popupShowOnStartupInput, "켜면 EXE 실행 또는 Windows 로그인 때 팝업을 바로 표시합니다. 끄면 트레이 아이콘을 클릭할 때까지 숨겨 둡니다.");
            helpTip.SetToolTip(diskList, "동시에 감시할 드라이브를 여러 개 선택합니다. 각 드라이브의 디스크 사용 시간을 별도 항목과 그래프로 표시합니다.");
        }

        private void LoadMetricRows()
        {
            loading = true;
            metricGrid.Rows.Clear();
            foreach (MetricOption option in working.Metrics.OrderBy(delegate(MetricOption m) { return m.Order; }))
            {
                int index = metricGrid.Rows.Add(option.Enabled, option.DisplayName, option.Label, option.ShowTemperature,
                    "선택", option.ShowValue, option.ShowGraph, ValueFormatDisplay(option), StyleDisplay(option.GraphStyle), "선택");
                DataGridViewRow row = metricGrid.Rows[index];
                row.Tag = option;
                if (option.Kind != MetricKind.Cpu && option.Kind != MetricKind.Gpu)
                {
                    row.Cells["Temperature"].ReadOnly = true;
                    row.Cells["Temperature"].Style.ForeColor = Color.Gray;
                    row.Cells["Temperature"].Style.BackColor = Color.FromArgb(238, 238, 238);
                    row.Cells["TemperatureColor"].ReadOnly = true;
                    row.Cells["TemperatureColor"].Style.ForeColor = Color.Gray;
                    row.Cells["TemperatureColor"].Style.BackColor = Color.FromArgb(238, 238, 238);
                }
                else
                {
                    row.Cells["TemperatureColor"].Style.BackColor = option.TemperatureColor;
                    row.Cells["TemperatureColor"].Style.SelectionBackColor = option.TemperatureColor;
                    row.Cells["TemperatureColor"].Style.ForeColor = ContrastColor(option.TemperatureColor);
                    row.Cells["TemperatureColor"].Style.SelectionForeColor = ContrastColor(option.TemperatureColor);
                }
                if (option.Kind != MetricKind.Memory)
                {
                    row.Cells["Format"].ReadOnly = true;
                    row.Cells["Format"].Style.ForeColor = Color.Gray;
                }
                row.Cells["Color"].Style.BackColor = option.Color;
                row.Cells["Color"].Style.SelectionBackColor = option.Color;
                row.Cells["Color"].Style.ForeColor = ContrastColor(option.Color);
                row.Cells["Color"].Style.SelectionForeColor = ContrastColor(option.Color);
            }
            loading = false;
        }

        private void MetricGridCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            string columnName = metricGrid.Columns[e.ColumnIndex].Name;
            if (columnName != "Color" && columnName != "TemperatureColor") return;
            MetricOption option = (MetricOption)metricGrid.Rows[e.RowIndex].Tag;
            bool temperatureColor = columnName == "TemperatureColor";
            if (temperatureColor && option.Kind != MetricKind.Cpu && option.Kind != MetricKind.Gpu) return;
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.Color = temperatureColor ? option.TemperatureColor : option.Color;
                dialog.FullOpen = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (temperatureColor) option.TemperatureColorArgb = dialog.Color.ToArgb();
                    else option.ColorArgb = dialog.Color.ToArgb();
                    DataGridViewCell colorCell = metricGrid.Rows[e.RowIndex].Cells[columnName];
                    colorCell.Style.BackColor = dialog.Color;
                    colorCell.Style.SelectionBackColor = dialog.Color;
                    colorCell.Style.ForeColor = ContrastColor(dialog.Color);
                    colorCell.Style.SelectionForeColor = ContrastColor(dialog.Color);
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
            foreach (Control control in new Control[] { intervalInput, historyInput, widthInput, offsetInput, insideItemWidthInput, insideHeightInput, popupWidthInput, popupHeightInput, opacityInput, fontInput })
                ((NumericUpDown)control).ValueChanged += delegate { RefreshPreview(); };
            positionInput.SelectedIndexChanged += delegate
            {
                if (positionInput.SelectedIndex == 2 && !widgetInteractionInput.Checked)
                    widgetInteractionInput.Checked = true;
                UpdateModeControlAvailability();
                RefreshPreview();
            };
            fullscreenInput.SelectedIndexChanged += delegate { RefreshPreview(); };
            floatingOrderInput.SelectedIndexChanged += delegate { RefreshPreview(); };
            hiddenMeasurementInput.SelectedIndexChanged += delegate { RefreshPreview(); };
            autoFitInput.CheckedChanged += delegate { RefreshPreview(); };
            seamlessInput.CheckedChanged += delegate { RefreshPreview(); };
            widgetInteractionInput.CheckedChanged += delegate { RefreshPreview(); };
            overflowPagingInput.CheckedChanged += delegate { RefreshPreview(); };
            popupPinnedInput.CheckedChanged += delegate { RefreshPreview(); };
            popupShowOnStartupInput.CheckedChanged += delegate { RefreshPreview(); };
            diskList.ItemCheck += delegate { BeginInvoke((MethodInvoker)delegate { RefreshPreview(); }); };
            WireDropDownPause(positionInput);
            WireDropDownPause(fullscreenInput);
            WireDropDownPause(floatingOrderInput);
            WireDropDownPause(hiddenMeasurementInput);
            metricGrid.EditingControlShowing += MetricGridEditingControlShowing;
        }

        private void WireDropDownPause(ComboBox combo)
        {
            combo.DropDown += ComboDropDown;
            combo.DropDownClosed += ComboDropDownClosed;
        }

        private void MetricGridEditingControlShowing(object sender, DataGridViewEditingControlShowingEventArgs e)
        {
            ComboBox combo = e.Control as ComboBox;
            if (combo == null) return;
            combo.DropDown -= ComboDropDown;
            combo.DropDownClosed -= ComboDropDownClosed;
            combo.DropDown += ComboDropDown;
            combo.DropDownClosed += ComboDropDownClosed;
        }

        private void ComboDropDown(object sender, EventArgs e)
        {
            dropDownOpen = true;
        }

        private void ComboDropDownClosed(object sender, EventArgs e)
        {
            dropDownOpen = false;
            BeginInvoke((MethodInvoker)delegate { RefreshPreview(); });
        }

        private void ReadControlsToWorking()
        {
            foreach (DataGridViewRow row in metricGrid.Rows)
            {
                MetricOption option = (MetricOption)row.Tag;
                option.Enabled = Convert.ToBoolean(row.Cells["Enabled"].Value ?? false);
                option.Label = Convert.ToString(row.Cells["Label"].Value) ?? option.DisplayName;
                option.ShowTemperature = (option.Kind == MetricKind.Cpu || option.Kind == MetricKind.Gpu) &&
                    Convert.ToBoolean(row.Cells["Temperature"].Value ?? false);
                option.ShowValue = Convert.ToBoolean(row.Cells["Value"].Value ?? false);
                option.ShowGraph = Convert.ToBoolean(row.Cells["Graph"].Value ?? false);
                if (option.Kind == MetricKind.Memory)
                    option.ValueFormat = ValueFormatValue(Convert.ToString(row.Cells["Format"].Value));
                option.GraphStyle = StyleValue(Convert.ToString(row.Cells["Style"].Value));
                option.Order = row.Index;
            }
            working.UpdateIntervalMs = (int)intervalInput.Value;
            working.HistorySeconds = (int)historyInput.Value;
            working.MaxWidth = (int)widthInput.Value;
            working.TaskbarOffset = (int)offsetInput.Value;
            working.InsideItemWidth = (int)insideItemWidthInput.Value;
            working.InsideHeight = (int)insideHeightInput.Value;
            working.PopupWidth = (int)popupWidthInput.Value;
            working.PopupHeight = (int)popupHeightInput.Value;
            working.OpacityPercent = (int)opacityInput.Value;
            working.FontSize = (float)fontInput.Value;
            working.PositionMode = positionInput.SelectedIndex == 2 ? "Popup" : (positionInput.SelectedIndex == 1 ? "Above" : "Inside");
            working.FullscreenMode = fullscreenInput.SelectedIndex == 1 ? "Show" : (fullscreenInput.SelectedIndex == 2 ? "ClickThrough" : "Hide");
            working.FloatingZOrder = floatingOrderInput.SelectedIndex == 1 ? "Top" :
                (floatingOrderInput.SelectedIndex == 2 ? "Bottom" : "Normal");
            working.AutoFit = autoFitInput.Checked;
            working.HiddenMeasurementMode = hiddenMeasurementInput.SelectedIndex == 1 ? "Throttle" :
                (hiddenMeasurementInput.SelectedIndex == 2 ? "Continue" : "Stop");
            working.PauseWhenHidden = !String.Equals(working.HiddenMeasurementMode, "Continue", StringComparison.OrdinalIgnoreCase);
            working.StartWithWindows = startupInput.Checked;
            working.ShowSettingsOnManualLaunch = showSettingsInput.Checked;
            working.InsideStyle = seamlessInput.Checked ? "Seamless" : "Panel";
            working.WidgetInteractionEnabled = widgetInteractionInput.Checked;
            working.OverflowPaging = overflowPagingInput.Checked;
            working.PopupPinned = popupPinnedInput.Checked;
            working.PopupShowOnStartup = popupShowOnStartupInput.Checked;
            working.SelectedDisks = diskList.CheckedItems.Cast<object>().Select(delegate(object item) { return Convert.ToString(item); })
                .Where(delegate(string name) { return !String.IsNullOrWhiteSpace(name); }).ToList();
            if (working.SelectedDisks.Count == 0) working.SelectedDisks.AddRange(AppSettings.GetAvailableDiskNames().Take(1));
        }

        private void RefreshPreview()
        {
            if (loading || dropDownOpen) return;
            try
            {
                ReadControlsToWorking();
                bool inside = working.PositionMode == "Inside";
                bool seamless = inside && String.Equals(working.InsideStyle, "Seamless", StringComparison.OrdinalIgnoreCase);
                preview.SetIntegratedStyle(inside, Color.FromArgb(31, 31, 31), seamless);
                preview.Configure(working, lastSnapshot, lastHistory);
                preview.Width = GetPreviewWidth(inside);
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
            // Do not repaint the live graph while the user is manipulating this
            // form. Control-triggered RefreshPreview calls still apply instantly.
            if (dropDownOpen || ContainsFocus || metricGrid.IsCurrentCellInEditMode) return;
            bool inside = working.PositionMode == "Inside";
            bool seamless = inside && String.Equals(working.InsideStyle, "Seamless", StringComparison.OrdinalIgnoreCase);
            preview.SetIntegratedStyle(inside, Color.FromArgb(31, 31, 31), seamless);
            preview.Configure(working, snapshot, history);
            preview.Width = GetPreviewWidth(inside);
            preview.Height = inside ? working.InsideHeight : 48;
        }

        private int GetPreviewWidth(bool inside)
        {
            int containerWidth = Math.Max(1, preview.Parent.ClientSize.Width - 20);
            int preferredWidth = preview.GetPreferredWidth();
            if (inside && working.AutoFit)
            {
                try
                {
                    IntPtr taskbarHandle = NativeMethods.GetPrimaryTaskbarHandle();
                    Rectangle taskbar = NativeMethods.GetPrimaryTaskbarBounds();
                    TaskbarFreeSlot freeSlot = TaskbarLayoutProbe.GetFreeSlot(taskbarHandle, taskbar, working.TaskbarOffset);
                    int rightLimit = Math.Min(freeSlot.Right, taskbar.Width - 8);
                    int availableWidth = Math.Max(1, rightLimit - freeSlot.Left);
                    preferredWidth = WidgetForm.CalculateInsideWidgetWidth(true, working.MaxWidth,
                        preferredWidth, availableWidth);
                }
                catch
                {
                }
            }
            return Math.Max(1, Math.Min(containerWidth, preferredWidth));
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
            applyStatus.Text = "저장·표시 적용됨";
        }

        private void ApplyOnly(object sender, EventArgs e)
        {
            ReadControlsToWorking();
            host.ApplySettings(working, false);
            RefreshPreview();
            applyStatus.Text = "적용됨";
        }

        private void ResetDefaults(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show(this,
                "표시 항목, 색상, 크기, 동작 설정을 처음 설치 상태로 되돌릴까요?\n\n적용 또는 저장 버튼을 누르기 전에는 실제 설정에 저장되지 않습니다.",
                "기본 설정 복원", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes) return;

            working = AppSettings.CreateDefault();
            LoadControlsFromWorking();
            applyStatus.Text = "기본값 불러옴 · 적용 전에는 저장되지 않음";
        }

        private void LoadControlsFromWorking()
        {
            loading = true;
            try
            {
                intervalInput.Value = working.UpdateIntervalMs;
                historyInput.Value = working.HistorySeconds;
                widthInput.Value = working.MaxWidth;
                offsetInput.Value = working.TaskbarOffset;
                insideItemWidthInput.Value = working.InsideItemWidth;
                insideHeightInput.Value = working.InsideHeight;
                popupWidthInput.Value = working.PopupWidth;
                popupHeightInput.Value = working.PopupHeight;
                opacityInput.Value = working.OpacityPercent;
                fontInput.Value = (decimal)working.FontSize;
                positionInput.SelectedIndex = working.PositionMode == "Popup" ? 2 : (working.PositionMode == "Above" ? 1 : 0);
                fullscreenInput.SelectedIndex = working.FullscreenMode == "Show" ? 1 : (working.FullscreenMode == "ClickThrough" ? 2 : 0);
                floatingOrderInput.SelectedIndex = String.Equals(working.FloatingZOrder, "Top", StringComparison.OrdinalIgnoreCase) ? 1 :
                    (String.Equals(working.FloatingZOrder, "Bottom", StringComparison.OrdinalIgnoreCase) ? 2 : 0);
                autoFitInput.Checked = working.AutoFit;
                hiddenMeasurementInput.SelectedIndex = String.Equals(working.HiddenMeasurementMode, "Throttle", StringComparison.OrdinalIgnoreCase) ? 1 :
                    (String.Equals(working.HiddenMeasurementMode, "Continue", StringComparison.OrdinalIgnoreCase) ? 2 : 0);
                startupInput.Checked = working.StartWithWindows;
                showSettingsInput.Checked = working.ShowSettingsOnManualLaunch;
                seamlessInput.Checked = String.Equals(working.InsideStyle, "Seamless", StringComparison.OrdinalIgnoreCase);
                widgetInteractionInput.Checked = working.WidgetInteractionEnabled;
                overflowPagingInput.Checked = working.OverflowPaging;
                popupPinnedInput.Checked = working.PopupPinned;
                popupShowOnStartupInput.Checked = working.PopupShowOnStartup;
                for (int index = 0; index < diskList.Items.Count; index++)
                {
                    string diskName = Convert.ToString(diskList.Items[index]);
                    diskList.SetItemChecked(index, working.SelectedDisks.Contains(diskName, StringComparer.OrdinalIgnoreCase));
                }
            }
            finally
            {
                loading = false;
            }
            LoadMetricRows();
            UpdateModeControlAvailability();
            RefreshPreview();
        }

        public void SyncPopupSize(int width, int height)
        {
            loading = true;
            try
            {
                working.PopupWidth = Math.Max((int)popupWidthInput.Minimum, Math.Min((int)popupWidthInput.Maximum, width));
                working.PopupHeight = Math.Max((int)popupHeightInput.Minimum, Math.Min((int)popupHeightInput.Maximum, height));
                popupWidthInput.Value = working.PopupWidth;
                popupHeightInput.Value = working.PopupHeight;
                applyStatus.Text = "팝업 크기 저장됨";
            }
            finally
            {
                loading = false;
            }
            RefreshPreview();
        }

        private void UpdateModeControlAvailability()
        {
            bool popup = positionInput.SelectedIndex == 2;
            bool floating = positionInput.SelectedIndex != 0;
            popupWidthInput.Enabled = popup;
            popupHeightInput.Enabled = popup;
            popupPinnedInput.Enabled = popup;
            popupShowOnStartupInput.Enabled = popup;
            floatingOrderInput.Enabled = floating;
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

        private static string ValueFormatDisplay(MetricOption option)
        {
            if (option == null || option.Kind != MetricKind.Memory) return "기본";
            if (String.Equals(option.ValueFormat, "UsedGb", StringComparison.OrdinalIgnoreCase)) return "사용 GB";
            if (String.Equals(option.ValueFormat, "UsedTotalGb", StringComparison.OrdinalIgnoreCase)) return "사용/전체 GB";
            return "%";
        }

        private static string ValueFormatValue(string value)
        {
            if (value == "사용 GB") return "UsedGb";
            if (value == "사용/전체 GB") return "UsedTotalGb";
            return "Percent";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && helpTip != null) helpTip.Dispose();
            base.Dispose(disposing);
        }
    }
}
