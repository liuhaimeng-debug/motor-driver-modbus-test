# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

电机驱动板 Modbus RTU 串口通信测试工具，单文件 C# WinForms 应用。

## 构建

```bash
dotnet publish -c Release -o publish -r win-x64 --self-contained true
```

输出单文件 exe（~160MB，内置完整 .NET 8 运行时），可在任何 Windows 机器直接运行。

源文件：`MotorDriverTest.cs`（单类 `MainForm`）。

## 协议

Modbus RTU，38400bps, 8,N,1。寄存器地址偏移（代码中用）：

| 偏移 | 名称 | 读写 | 说明 |
|------|------|------|------|
| 0x0000 | 站号 | 写 | 1~255 |
| 0x0001 | 电流 | 写 | 0x00=100% ~ 0x0F=6.25% |
| 0x0002 | 细分 | 写 | 0x00=全步进 ~ 0x0A=1/256 |
| 0x0003 | 运行/停止 | 写 | 0xFF00=运行, 0x0000=停止 |
| 0x0004 | 方向 | 写 | 0xFF00=正向, 0x0000=反向 |
| 0x0005 | 速度 | 写 | 值×100=rpm，最大 0x4E20 |
| 0x0006 | 报警 | 只读 | 0x0000=正常 |
| 0x0007 | 实际转速 | 只读 | 值×100=rpm |

## 代码结构

- `CalcCRC16()` — Modbus CRC-16，多项式 0xA001，输出**小端序**（低字节在前），与硬编码命令一致
- `SendReadCmd(addr, reg)` — 发送 0x03 读命令，更新 `_lastReadReg` 用于响应解析
- `ParseAndShow()` — 解析接收缓冲区，区分广播(0x00地址)、写响应(0x06/0x10)、读响应(0x03/0x04)
- `ParseReadResponse(data)` — 根据 `_lastReadReg` 分支更新状态标签
- `SendRaw()` / `SendCustom()` — 发送命令，清缓冲区，显示 TX 记录
- 3 秒 Timer 轮询报警(0x0006)和转速(0x0007)

## 注意事项

- CRC 字节序为**小端序**（如启动命令 CRC 字段为 `3F C2`，即 0xC23F），新增命令需保持一致
- 广播地址常量为 `0x00`（地址字节检查 `data[0]`，非功能码 `data[1]`）
- `System.IO.Ports` 通过 NuGet 包引用，发布时随 exe 一起携带
