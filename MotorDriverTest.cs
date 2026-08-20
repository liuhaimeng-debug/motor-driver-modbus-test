using System;
using System.Drawing;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace MotorDriverTest
{
    public class MainForm : Form
    {
        private SerialPort _serialPort;
        private byte[] _receiveBuffer = new byte[0];

        // 广播地址
        private const byte BROADCAST_ADDR = 0x00;

        // 启动命令: 01 06 00 03 FF 00 38 3A
        private readonly byte[] _startCmd = { 0x01, 0x06, 0x00, 0x03, 0xFF, 0x00, 0x38, 0x3A };
        // 停止命令: 01 06 00 03 00 00 79 CA
        private readonly byte[] _stopCmd = { 0x01, 0x06, 0x00, 0x03, 0x00, 0x00, 0x79, 0xCA };

        // 状态标签引用
        private Label _lblAlarmStatus, _lblSpeedStatus;
        // 记录最近一次读取的寄存器地址和数量，用于响应解析
        private ushort _lastReadReg = 0xFFFF;
        private int _lastReadCount = 1;

        public MainForm()
        {
            SetupUI();
        }

        private void SetupUI()
        {
            this.Text = "电机驱动板测试";
            this.Size = new Size(720, 620);
            this.MinimumSize = new Size(520, 500);
            this.StartPosition = FormStartPosition.CenterScreen;

            // ---- 串口设置栏 ----
            var panel = new FlowLayoutPanel
            {
                Location = new Point(10, 10),
                Size = new Size(690, 40),
                WrapContents = false,
                BackColor = Color.FromArgb(240, 240, 240)
            };

            var lblPort = new Label { Text = "端口:", AutoSize = true, Margin = new Padding(0, 4, 4, 4) };
            var cboPorts = new ComboBox { Name = "cboPorts", Width = 120, Location = new Point(45, 8), DropDownStyle = ComboBoxStyle.DropDown };
            RefreshPorts(cboPorts);

            var lblParams = new Label
            {
                Text = "38400/8/N/1",
                Location = new Point(175, 8),
                Size = new Size(120, 25),
                Font = new Font(this.Font.FontFamily, 9f, FontStyle.Bold),
                ForeColor = Color.Gray
            };

            var btnOpen = new Button { Text = "打开串口", Width = 80, Location = new Point(310, 8) };
            btnOpen.Click += (s, e) =>
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    ClosePort();
                    btnOpen.Text = "打开串口";
                    btnOpen.ForeColor = Color.Black;
                }
                else
                {
                    OpenPort(cboPorts, btnOpen);
                }
            };

            var btnRefresh = new Button { Text = "刷新", Width = 55, Location = new Point(400, 8) };
            btnRefresh.Click += (s, e) => RefreshPorts(cboPorts);

            panel.Controls.AddRange(new Control[] { lblPort, cboPorts, lblParams, btnOpen, btnRefresh });
            this.Controls.Add(panel);

            // ---- 快捷按钮 ----
            var grpQuick = new GroupBox { Text = "快捷操作", Location = new Point(10, 60), Size = new Size(690, 40) };
            var btnStart = new Button { Text = "启动", Width = 70, Location = new Point(10, 15), BackColor = Color.LightGreen, Font = new Font(this.Font.FontFamily, 10f, FontStyle.Bold) };
            btnStart.Click += (s, e) => SendRaw(_startCmd, btnStart, "启动");

            var btnStop = new Button { Text = "停止", Width = 70, Location = new Point(90, 15), BackColor = Color.LightCoral, Font = new Font(this.Font.FontFamily, 10f, FontStyle.Bold) };
            btnStop.Click += (s, e) => SendRaw(_stopCmd, btnStop, "停止");

            grpQuick.Controls.AddRange(new Control[] { btnStart, btnStop });
            this.Controls.Add(grpQuick);

            // ---- 参数设置 ----
            var grpParam = new GroupBox { Text = "参数设置", Location = new Point(10, 110), Size = new Size(690, 50) };

            var lblCurrent = new Label { Text = "电流:", Location = new Point(10, 17), Size = new Size(30, 20) };
            var cboCurrent = new ComboBox { Name = "cboCurrent", Location = new Point(45, 14), Width = 100, DropDownStyle = ComboBoxStyle.DropDown };
            var currentValues = new[] { "100%", "93.75%", "87.5%", "81.25%", "75%", "68.75%", "62.5%", "56.25%", "50%", "43.75%", "37.5%", "31.25%", "25%", "18.75%", "12.5%", "6.25%" };
            for (int i = 0; i < 16; i++)
            {
                cboCurrent.Items.Add(currentValues[i]);
                if (i == 15) cboCurrent.SelectedIndex = i; // 默认 6.25% (0x000F)
            }
            grpParam.Controls.Add(lblCurrent);
            grpParam.Controls.Add(cboCurrent);

            var lblStep = new Label { Text = "细分:", Location = new Point(160, 17), Size = new Size(30, 20) };
            var cboStep = new ComboBox { Name = "cboStep", Location = new Point(195, 14), Width = 120, DropDownStyle = ComboBoxStyle.DropDown };
            var stepValues = new[] { "全步进(100%)", "71%", "非循环1/2", "1/2", "1/4", "1/8", "1/16", "1/32", "1/64", "1/128", "1/256" };
            for (int i = 0; i < stepValues.Length; i++)
            {
                cboStep.Items.Add(stepValues[i]);
                if (i == 10) cboStep.SelectedIndex = i; // 默认 1/256 (0x000A)
            }
            grpParam.Controls.Add(lblStep);
            grpParam.Controls.Add(cboStep);

            var lblSpeed = new Label { Text = "速度(rpm):", Location = new Point(330, 17), Size = new Size(60, 20) };
            var nudSpeed = new NumericUpDown { Name = "nudSpeed", Location = new Point(395, 14), Width = 80, Minimum = 0, Maximum = 20000, Value = 5000 };
            grpParam.Controls.Add(lblSpeed);
            grpParam.Controls.Add(nudSpeed);

            var lblDir = new Label { Text = "方向:", Location = new Point(490, 17), Size = new Size(30, 20) };
            var cboDir = new ComboBox { Name = "cboDir", Location = new Point(525, 14), Width = 70, DropDownStyle = ComboBoxStyle.DropDown };
            cboDir.Items.Add("正向");
            cboDir.Items.Add("反向");
            cboDir.SelectedIndex = 0; // 默认正向
            grpParam.Controls.Add(lblDir);
            grpParam.Controls.Add(cboDir);

            var btnSendParam = new Button { Text = "发送参数", Width = 80, Location = new Point(610, 12), BackColor = Color.LightBlue };
            btnSendParam.Click += (s, e) =>
            {
                if (_serialPort == null || !_serialPort.IsOpen)
                {
                    MessageBox.Show("请先打开串口！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                byte current = (byte)cboCurrent.SelectedIndex;
                byte step = (byte)cboStep.SelectedIndex;
                ushort speed = (ushort)nudSpeed.Value;
                byte dir = cboDir.SelectedIndex == 0 ? (byte)0xFF : (byte)0x00;

                // 0x10 写多个寄存器: addr=0x01, func=0x10, reg=0x0001, qty=0x0005, bytes=0x0A
                // 写5个寄存器(0x0001~0x0005): 电流(0x0001) 细分(0x0002) 运行/停止(0x0003)
                //                   方向(0x0004) 速度(0x0005)
                // 0x0006/0x0007为只读寄存器，不可写入
                byte[] data = new byte[]
                {
                    0x00, current,              // 电流 0x0001
                    0x00, step,                 // 细分 0x0002
                    0x00, 0x00,                 // 运行/停止 0x0003
                    dir, 0x00,                  // 方向 0x0004  (正向=0xFF00, 反向=0x0000)
                    (byte)(speed >> 8), (byte)(speed & 0xFF)  // 速度 0x0005
                };
                byte[] cmd = new byte[7 + data.Length + 2];
                cmd[0] = 0x01; cmd[1] = 0x10;
                cmd[2] = 0x00; cmd[3] = 0x01; // 起始地址 0x0001
                cmd[4] = 0x00; cmd[5] = 0x05; // 寄存器数量 5
                cmd[6] = (byte)data.Length;
                Array.Copy(data, 0, cmd, 7, data.Length);
                ushort crc = CalcCRC16(cmd, 7 + data.Length);
                cmd[7 + data.Length] = (byte)(crc & 0xFF);
                cmd[8 + data.Length] = (byte)((crc >> 8) & 0xFF);

                SendRaw(cmd, btnSendParam, "发送参数");
            };
            grpParam.Controls.Add(btnSendParam);
            this.Controls.Add(grpParam);

            // ---- 实时状态 ----
            var grpStatus = new GroupBox { Text = "实时状态", Location = new Point(10, 170), Size = new Size(690, 70) };
            _lblAlarmStatus = new Label { Text = "报警: --", Location = new Point(10, 17), Size = new Size(120, 20), Font = new Font(this.Font.FontFamily, 9f) };
            _lblSpeedStatus = new Label { Text = "实际转速: --", Location = new Point(150, 17), Size = new Size(160, 20), Font = new Font(this.Font.FontFamily, 9f) };
            var btnQueryAll = new Button { Text = "查询全部", Width = 80, Location = new Point(605, 12), BackColor = Color.LightBlue };
            btnQueryAll.Click += (s, e) =>
            {
                if (_serialPort == null || !_serialPort.IsOpen) { MessageBox.Show("请先打开串口！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                QueryAllStatus();
            };
            grpStatus.Controls.AddRange(new Control[] { _lblAlarmStatus, _lblSpeedStatus, btnQueryAll });
            this.Controls.Add(grpStatus);

            // ---- 自定义命令 ----
            var lblCmd = new Label { Text = "自定义命令 (空格分隔十六进制):", Location = new Point(10, 265), Size = new Size(670, 20) };
            var txtCommand = new TextBox
            {
                Name = "txtCommand",
                Location = new Point(10, 287),
                Size = new Size(500, 25),
                Text = ""
            };

            var btnSend = new Button { Text = "发送", Width = 70, Location = new Point(520, 285) };
            btnSend.Click += (s, e) => SendCustom(txtCommand, btnSend);

            var chkLoop = new CheckBox { Text = "循环(秒):", Location = new Point(600, 287), AutoSize = true };
            var nudLoop = new NumericUpDown { Location = new Point(665, 287), Width = 55, Minimum = 0, Maximum = 3600, Value = 0 };
            chkLoop.CheckedChanged += (s, e) => nudLoop.Enabled = chkLoop.Checked;

            var timerLoop = new System.Windows.Forms.Timer { Interval = 1000 };
            timerLoop.Tick += (s, e) => { if (nudLoop.Value > 0) SendCustom(txtCommand, btnSend); };

            this.Controls.AddRange(new Control[] { lblCmd, txtCommand, btnSend, chkLoop, nudLoop });

            // ---- 接收区 ----
            var lblRecv = new Label { Text = "接收数据:", Location = new Point(10, 322), Size = new Size(670, 20) };
            this.Controls.Add(lblRecv);

            var dgv = new DataGridView
            {
                Name = "dgvReceived",
                Location = new Point(10, 344),
                Size = new Size(690, 180),
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false,
                Font = new Font("Consolas", 9f),
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.Fixed3D
            };
            dgv.Columns.Add("Index", "序号");
            dgv.Columns["Index"].Width = 60;
            dgv.Columns.Add("Hex", "十六进制");
            dgv.Columns["Hex"].Width = 320;
            dgv.Columns.Add("Time", "时间");
            dgv.Columns["Time"].Width = 100;
            this.Controls.Add(dgv);

            // ---- 底部按钮 ----
            var btnClear = new Button { Text = "清空", Width = 70, Location = new Point(10, 538) };
            btnClear.Click += (s, e) => dgv.Invoke(new Action(() => dgv.Rows.Clear()));

            var btnSave = new Button { Text = "保存", Width = 70, Location = new Point(90, 538) };
            btnSave.Click += (s, e) =>
            {
                var dlg = new SaveFileDialog { Filter = "文本文件|*.txt|CSV文件|*.csv", DefaultExt = "txt" };
                if (dlg.ShowDialog() != DialogResult.OK) return;
                using (var sw = new System.IO.StreamWriter(dlg.FileName, false, Encoding.UTF8))
                {
                    sw.WriteLine("序号\t时间\t发送/接收\t十六进制");
                    foreach (DataGridViewRow row in dgv.Rows)
                    {
                        if (row.Cells[0].Value != null)
                            sw.WriteLine(row.Cells[0].Value + "\t" + row.Cells[2].Value + "\t" + row.Cells[1].Value);
                    }
                }
                MessageBox.Show("保存完成！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            var lblClock = new Label
            {
                Text = "时钟: --:--:--",
                Location = new Point(170, 538),
                Size = new Size(120, 20),
                Font = new Font(this.Font.FontFamily, 9f, FontStyle.Bold)
            };
            var timerClock = new System.Windows.Forms.Timer { Interval = 1000 };
            timerClock.Tick += (s, e) => lblClock.Text = "时钟: " + DateTime.Now.ToString("HH:mm:ss");

            var lblStatus = new Label
            {
                Text = "未连接",
                Location = new Point(500, 541),
                Size = new Size(180, 20),
                Font = new Font(this.Font.FontFamily, 9f, FontStyle.Bold)
            };
            lblStatus.ForeColor = Color.Red;

            this.Controls.AddRange(new Control[] { btnClear, btnSave, lblClock, lblStatus });

            timerClock.Start();

            // 引用
            dgv.Tag = lblStatus;
            txtCommand.Tag = dgv;

            this.FormClosing += (s, e) => ClosePort();
        }

        private void RefreshPorts(ComboBox cbo)
        {
            string cur = cbo.Text;
            cbo.Items.Clear();
            foreach (string p in SerialPort.GetPortNames())
                cbo.Items.Add(p);
            if (string.IsNullOrEmpty(cur) && cbo.Items.Count > 0)
                cbo.Text = cbo.Items[0].ToString();
        }

        private void OpenPort(ComboBox cboPorts, Button btn)
        {
            try
            {
                _serialPort = new SerialPort
                {
                    PortName = cboPorts.Text,
                    BaudRate = 38400,
                    DataBits = 8,
                    StopBits = StopBits.One,
                    Parity = Parity.None,
                    WriteTimeout = 2000,
                    ReadTimeout = 5000
                };
                _serialPort.Open();

                btn.Text = "关闭串口";
                btn.ForeColor = Color.DarkRed;

                DataGridView dgv = GetDgv();
                Label lbl = dgv.Tag as Label;
                if (lbl != null)
                {
                    lbl.Invoke(new Action(() =>
                    {
                        lbl.Text = "已连接 " + _serialPort.PortName + " @ 38400/8/N/1";
                        lbl.ForeColor = Color.Green;
                    }));
                }

                _serialPort.DataReceived += OnDataReceived;
            }
            catch (Exception ex)
            {
                MessageBox.Show("打开串口失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ClosePort()
        {
            if (_serialPort != null)
            {
                _serialPort.DataReceived -= OnDataReceived;
                _serialPort.Close();
                _serialPort.Dispose();
                _serialPort = null;
            }
        }

        private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (_serialPort == null) return;
            System.Threading.Thread.Sleep(100);
            int n = _serialPort.BytesToRead;
            if (n == 0) return;

            byte[] buf = new byte[n];
            _serialPort.Read(buf, 0, n);

            byte[] nb = new byte[_receiveBuffer.Length + n];
            Array.Copy(_receiveBuffer, 0, nb, 0, _receiveBuffer.Length);
            Array.Copy(buf, 0, nb, _receiveBuffer.Length, n);
            _receiveBuffer = nb;

            ParseAndShow();
        }

        private void ParseAndShow()
        {
            if (_receiveBuffer.Length < 5) return;

            DataGridView dgv = GetDgv();
            if (dgv == null) return;

            bool hasData = false;

            // 广播地址检查: 地址字节在 [0]
            if (_receiveBuffer[0] == BROADCAST_ADDR)
            {
                if (_receiveBuffer.Length >= 5)
                {
                    int take = Math.Min(_receiveBuffer.Length, 5);
                    byte[] slice = new byte[take];
                    Array.Copy(_receiveBuffer, 0, slice, 0, take);
                    AppendRow(dgv, slice);
                    _receiveBuffer = new byte[0];
                    hasData = true;
                }
            }
            else if (_receiveBuffer.Length >= 5)
            {
                int len;
                if (_receiveBuffer[1] == 0x10)
                    len = 6;
                else if (_receiveBuffer[1] == 0x03 || _receiveBuffer[1] == 0x04)
                {
                    if (_receiveBuffer.Length < 3) return;
                    len = 3 + _receiveBuffer[2] + 2;
                }
                else if (_receiveBuffer[1] == 0x06)
                {
                    // 写单个寄存器响应: 地址 06 AA AA DD DD CRC CRC = 7字节
                    len = 7;
                }
                else
                {
                    len = Math.Max(5, _receiveBuffer.Length);
                }

                if (_receiveBuffer.Length >= len)
                {
                    byte[] slice = new byte[len];
                    Array.Copy(_receiveBuffer, 0, slice, 0, len);
                    AppendRow(dgv, slice);

                    // 解析读响应，更新状态标签
                    if (_receiveBuffer[1] == 0x03 && len >= 5)
                    {
                        ParseReadResponse(slice);
                    }

                    int remain = _receiveBuffer.Length - len;
                    if (remain > 0)
                    {
                        byte[] rest = new byte[remain];
                        Array.Copy(_receiveBuffer, len, rest, 0, remain);
                        _receiveBuffer = rest;
                    }
                    else
                    {
                        _receiveBuffer = new byte[0];
                    }
                    hasData = true;
                }
            }

            if (hasData)
            {
                dgv.Invoke(new Action(() => { if (dgv.RowCount > 0) dgv.FirstDisplayedScrollingRowIndex = dgv.RowCount - 1; }));
            }
        }

        /// <summary>
        /// 解析 0x03 读响应，更新状态标签
        /// </summary>
        private void ParseReadResponse(byte[] data)
        {
            // data: addr(1) func(1) len(1) valH(1) valL(1) crcH(1) crcL(1)
            if (data.Length < 5) return;
            int byteCount = data[2];
            if (data.Length < 3 + byteCount + 2) return;

            int count = _lastReadCount > 1 ? _lastReadCount : 1;

            for (int i = 0; i < count; i++)
            {
                if ((i + 1) * 2 > byteCount) break;
                ushort v = (ushort)((data[3 + i * 2] << 8) | data[4 + i * 2]);
                ushort reg = (ushort)(_lastReadReg + i);

                switch (reg)
                {
                    case 0x0004: // 方向 (已在ParseReadResponse的switch外，由循环统一处理)
                        break;
                    case 0x0006: // 报警
                        if (_lblAlarmStatus != null)
                        {
                            string alarmDesc = ParseAlarm(v);
                            _lblAlarmStatus.Invoke(new Action(() =>
                            {
                                _lblAlarmStatus.Text = "报警: " + alarmDesc;
                                _lblAlarmStatus.ForeColor = v == 0 ? Color.Green : Color.Red;
                            }));
                        }
                        break;
                    case 0x0007: // 实际转速
                        if (_lblSpeedStatus != null)
                        {
                            double rpm = v / 100.0;
                            _lblSpeedStatus.Invoke(new Action(() =>
                            {
                                _lblSpeedStatus.Text = "实际转速: " + rpm.ToString("F2") + " rpm";
                                _lblSpeedStatus.ForeColor = Color.Black;
                            }));
                        }
                        break;
                }
            }
        }

        private string ParseAlarm(ushort val)
        {
            switch (val)
            {
                case 0x0000: return "正常";
                case 0x0001: return "限位报警";
                case 0x0002: return "堵转报警";
                case 0x0003: return "编码器错误";
                case 0x0081: return "开路负载";
                case 0x0082: return "过热";
                case 0x0088: return "过流";
                case 0x0090: return "电荷泵欠压";
                case 0x00A0: return "电源欠压";
                case 0x00C0: return "SPI错误";
                default: return "0x" + val.ToString("X4");
            }
        }

        private void AppendRow(DataGridView dgv, byte[] data)
        {
            string hex = string.Join(" ", data.Select(b => b.ToString("X2")));
            string time = DateTime.Now.ToString("HH:mm:ss.fff");
            dgv.Invoke(new Action(() =>
            {
                dgv.Rows.Add(new object[] { dgv.RowCount + 1, hex, time });
                while (dgv.Rows.Count > 1000) dgv.Rows.RemoveAt(0);
            }));
        }

        /// <summary>
        /// 发送读命令 (功能码 0x03)
        /// </summary>
        private void SendReadCmd(byte addr, ushort reg, int count = 1)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return;
            byte[] cmd = new byte[6];
            cmd[0] = addr;
            cmd[1] = 0x03;
            cmd[2] = (byte)(reg >> 8);
            cmd[3] = (byte)(reg & 0xFF);
            cmd[4] = 0x00;
            cmd[5] = (byte)count; // 读 count 个寄存器
            ushort crc = CalcCRC16(cmd, cmd.Length);
            byte[] fullCmd = new byte[8];
            Array.Copy(cmd, 0, fullCmd, 0, 6);
            fullCmd[6] = (byte)(crc & 0xFF);
            fullCmd[7] = (byte)((crc >> 8) & 0xFF);
            _lastReadReg = reg;
            _lastReadCount = count;
            SendRaw(fullCmd, null, "读取");
        }

        /// <summary>
        /// 一条命令读全部寄存器(0x0000~0x0007)，只更新报警和实际转速
        /// </summary>
        private void QueryAllStatus()
        {
            SendReadCmd(0x01, 0x0000, 8);
        }

        private void SendRaw(byte[] cmd, Button btn, string originalText)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
            {
                MessageBox.Show("请先打开串口！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                _serialPort.Write(cmd, 0, cmd.Length);

                DataGridView dgv = GetDgv();
                if (dgv != null)
                {
                    string hex = string.Join(" ", cmd.Select(b => b.ToString("X2")));
                    string time = DateTime.Now.ToString("HH:mm:ss.fff");
                    dgv.Invoke(new Action(() =>
                    {
                        dgv.Rows.Add(new object[] { dgv.RowCount + 1, "TX: " + hex, time });
                        while (dgv.Rows.Count > 1000) dgv.Rows.RemoveAt(0);
                        if (dgv.RowCount > 0) dgv.FirstDisplayedScrollingRowIndex = dgv.RowCount - 1;
                    }));
                }

                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
                _receiveBuffer = new byte[0];

                if (btn != null)
                {
                    btn.Text = "已发送";
                    btn.Enabled = false;
                    Task.Delay(300).ContinueWith(_ => btn.Invoke(new Action(() => { btn.Text = originalText; btn.Enabled = true; })));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("发送失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SendCustom(TextBox txt, Button btn)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
            {
                MessageBox.Show("请先打开串口！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                string text = txt.Text.Trim();
                if (string.IsNullOrEmpty(text)) return;

                string[] parts = text.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                byte[] cmd = new byte[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                    cmd[i] = Convert.ToByte(parts[i], 16);

                _serialPort.Write(cmd, 0, cmd.Length);

                DataGridView dgv = GetDgv();
                if (dgv != null)
                {
                    string hex = string.Join(" ", cmd.Select(b => b.ToString("X2")));
                    string time = DateTime.Now.ToString("HH:mm:ss.fff");
                    dgv.Invoke(new Action(() =>
                    {
                        dgv.Rows.Add(new object[] { dgv.RowCount + 1, "TX: " + hex, time });
                        while (dgv.Rows.Count > 1000) dgv.Rows.RemoveAt(0);
                        if (dgv.RowCount > 0) dgv.FirstDisplayedScrollingRowIndex = dgv.RowCount - 1;
                    }));
                }

                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
                _receiveBuffer = new byte[0];

                if (btn != null)
                {
                    btn.Text = "已发送";
                    btn.Enabled = false;
                    Task.Delay(300).ContinueWith(_ => btn.Invoke(new Action(() => btn.Text = "发送")));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("发送失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Modbus CRC-16 计算（多项式 0xA001）
        /// </summary>
        private ushort CalcCRC16(byte[] data, int len)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < len; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x0001) != 0)
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    else
                        crc >>= 1;
                }
            }
            return crc;
        }

        private DataGridView GetDgv()
        {
            var txt = this.Controls.Find("txtCommand", false).FirstOrDefault() as TextBox;
            return txt.Tag as DataGridView;
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
