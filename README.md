# WorkReport

一个轻量、Local-first 的工作汇报与任务管理系统，面向个人或小团队的半月工作记录、任务跟踪、历史汇总与资料归档。

基于 **.NET 8 / ASP.NET Core Blazor / EF Core / SQLite** 构建，支持 Windows 本地运行与自包含发布。

## ✨ 功能特性

- **半月工作管理**：按 `01~15 / 16~月底` 周期组织任务，自动识别当前半月。
- **任务分类**：支持“重点工作 / 日常工作”分区展示。
- **状态统计**：任务总数、已完成、正在进行中、待测 / 待确认一屏汇总。
- **任务操作**：新增、编辑、复制、删除、详情查看。
- **继承未完成任务**：一键把上期未完成工作带入当前周期。
- **历史记录**：按历史周期查看已归档工作。
- **已完成汇总**：支持按期数、关键字、完成状态筛选和聚合查看。
- **Excel 导出**：使用 ClosedXML 生成格式化的半月工作汇报表。
- **文档库与附件**：任务可关联文档，支持浏览器打开和下载。
- **回收站**：任务采用软删除机制，可集中管理已删除记录。
- **用户系统**：支持登录、注册、个人资料和修改密码。
- **系统状态页**：查看数据库路径、文件大小、任务/用户数量、WAL/SHM、进程与监听地址等信息。
- **自动数据库迁移**：首次启动自动创建并升级 SQLite 数据库。
- **自动备份**：后台定时执行 SQLite WAL checkpoint，并保留最近 30 份数据库备份。
- **Windows x64 发布**：提供 `publish.bat`，可生成 self-contained 部署目录。

## 🖥️ 界面概览

主要导航包括：

- 当前半月
- 历史记录
- 已完成汇总
- 系统状态
- 文档库
- 回收站
- 个人资料 / 退出登录

当前半月页面提供周期切换、新增任务、继承上期未完成、Excel 导出，以及重点工作 / 日常工作的分区列表。

> 项目截图将在脱敏后补充，避免公开真实工作内容和客户/项目备注。

## 🧱 技术栈

| 模块 | 技术 |
| --- | --- |
| Runtime | .NET 8 |
| Web UI | ASP.NET Core Razor Components / Blazor Interactive Server |
| ORM | Entity Framework Core 8 |
| Database | SQLite |
| Excel | ClosedXML |
| Authentication | ASP.NET Core Cookie Authentication |
| Frontend | Razor / Bootstrap / CSS / Chart.js |
| Deployment | Windows x64 self-contained publish |

## 🚀 快速开始

### 1. 环境要求

安装 **.NET 8 SDK**。

### 2. 克隆仓库

```bash
git clone https://github.com/VkDream/WorkReport.git
cd WorkReport
```

### 3. 还原并运行

```bash
dotnet restore
dotnet run --launch-profile http
```

默认 HTTP 地址：

```text
http://localhost:51789
```

首次运行时程序会自动执行 EF Core Migration，并在项目运行目录创建：

```text
workreport.db
```

## 🔐 初始账号

首次数据库初始化时会创建默认管理员账号：

```text
用户名：admin
密码：admin123
```

首次登录后系统会强制要求修改默认密码。

> ⚠️ 如果将程序开放到局域网或公网，请先修改默认密码，并根据实际部署环境配置 HTTPS、反向代理及访问控制。

## 📊 Excel 导出

当前半月页面支持直接导出工作汇报 Excel。导出内容包含：

- 汇报周期
- 汇报人
- 重点工作 / 日常工作分区
- 工作说明
- 完成效果
- 解决时间
- 备注说明

导出功能由 **ClosedXML** 实现。

## 💾 数据与备份

默认数据库连接：

```text
Data Source=workreport.db
```

程序后台会定期备份数据库：

- 启动后约 30 秒执行首次备份检查
- 后续约每 6 小时执行一次
- 备份前尝试执行 WAL checkpoint
- 默认保留最近 30 个备份文件

备份目录位于程序运行目录下的：

```text
backups/
```

真实运行数据库、WAL/SHM、备份目录及发布产物不应提交到 Git 仓库。

## 📦 Windows 发布

仓库提供：

```text
publish.bat
```

直接执行即可生成 Windows x64 self-contained 版本：

```bat
publish.bat
```

默认输出到：

```text
publish/
```

发布脚本会尽量保留已有运行数据库、备份以及启动辅助文件。

## 📁 项目结构

```text
WorkReport/
├─ Components/          # Razor 页面、布局和共享组件
├─ Data/                # DbContext 与数据模型
├─ Migrations/          # EF Core Migration
├─ Services/            # 任务、认证、文档、状态与备份服务
├─ Properties/          # launchSettings
├─ wwwroot/             # CSS、Bootstrap、Chart.js、图标等静态资源
├─ Program.cs           # 应用启动、认证及 API
├─ WorkReport.csproj
├─ appsettings.json
└─ publish.bat
```

## 🛡️ 仓库数据安全

本仓库只提交源码与必要配置，以下运行数据已通过 `.gitignore` 排除：

```text
*.db
*.db-shm
*.db-wal
backups/
publish/
tmp/
.claude/
.reasonix/
```

请勿将包含真实工作记录、客户信息或账号数据的 SQLite 数据库提交到公开仓库。

---

**WorkReport** — 让半月工作记录、任务跟踪、汇报导出和资料归档集中在一个本地工具中。
