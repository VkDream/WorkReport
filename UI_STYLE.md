# WorkReport UI 设计规范

## 风格定位
现代简洁后台管理系统。白色主体，灰色层次，蓝色作为唯一强调色。
减少视觉噪音，让数据本身清晰可读。

## 色彩系统
```
--color-primary:     #3B82F6   /* 主蓝色，按钮/链接/强调 */
--color-primary-hover: #2563EB
--color-bg:          #F8FAFC   /* 页面背景，极浅灰 */
--color-surface:     #FFFFFF   /* 卡片/表格背景 */
--color-border:      #E2E8F0   /* 分割线、边框 */
--color-text-primary: #1E293B  /* 主文字 */
--color-text-secondary: #64748B /* 次要文字、标签 */
--color-text-muted:  #94A3B8   /* 占位符、暂无记录 */

/* 状态色 */
--color-success:     #10B981
--color-danger:      #EF4444
--color-warning:     #F59E0B
```

## 字体
```css
font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
```
- 页面标题：24px / font-weight 600
- 区块标题：16px / font-weight 600
- 正文：14px / font-weight 400
- 辅助文字：13px / color: var(--color-text-secondary)

## 布局结构
```
┌─────────────────────────────────────────────┐
│  顶部导航栏（白底，底部1px border，高度56px）  │
│  [工作汇报 logo]          [当前半月期 badge]  │
├──────────┬──────────────────────────────────┤
│  侧边栏  │  主内容区                          │
│  200px   │  padding: 24px                   │
│  白底    │  背景: #F8FAFC                    │
│  浅灰字  │                                  │
└──────────┴──────────────────────────────────┘
```

## 组件规范

### 侧边栏
```css
background: #FFFFFF;
border-right: 1px solid var(--color-border);
width: 200px;

/* 菜单项 */
.nav-item {
  padding: 10px 16px;
  border-radius: 8px;
  color: var(--color-text-secondary);
  font-size: 14px;
}
.nav-item.active {
  background: #EFF6FF;
  color: var(--color-primary);
  font-weight: 500;
}
```

### 卡片 / 内容区块
```css
background: #FFFFFF;
border: 1px solid var(--color-border);
border-radius: 12px;
padding: 20px 24px;
```

### 表格
```css
/* 表头 */
thead th {
  background: #F8FAFC;
  color: var(--color-text-secondary);
  font-size: 12px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  padding: 10px 16px;
  border-bottom: 1px solid var(--color-border);
}

/* 行 */
tbody tr {
  border-bottom: 1px solid var(--color-border);
  transition: background 0.1s;
}
tbody tr:hover {
  background: #F8FAFC;
}
tbody td {
  padding: 12px 16px;
  font-size: 14px;
  color: var(--color-text-primary);
}
```

### 按钮
```css
/* 主按钮 */
.btn-primary {
  background: var(--color-primary);
  color: white;
  border: none;
  border-radius: 8px;
  padding: 8px 16px;
  font-size: 14px;
  font-weight: 500;
}
.btn-primary:hover { background: var(--color-primary-hover); }

/* 文字按钮（操作列用） */
.btn-link-edit   { color: var(--color-primary); font-size: 13px; }
.btn-link-delete { color: var(--color-danger);  font-size: 13px; }
```

### Badge（数量标签）
```css
.badge-count {
  background: #EFF6FF;
  color: var(--color-primary);
  border-radius: 20px;
  padding: 2px 8px;
  font-size: 12px;
  font-weight: 600;
}
```

### 完成效果颜色（Result）
| 值 | 颜色 | 样式 |
|----|------|------|
| 完成（原 OK / PASS / 复判OK） | #10B981 | font-weight: 500 |
| 正在进行中（原 NG） | #F59E0B | font-weight: 600 |
| 待测 / 待确认 | #94A3B8 | 正常 |
| 空 | — | 显示 `—` |

### 空状态
```html
<div style="text-align:center; padding: 48px 0; color: #94A3B8;">
  <div style="font-size: 32px; margin-bottom: 8px;">📋</div>
  <div style="font-size: 14px;">暂无记录</div>
</div>
```

### 表单输入框
```css
.form-control {
  border: 1px solid var(--color-border);
  border-radius: 8px;
  padding: 8px 12px;
  font-size: 14px;
  transition: border-color 0.15s;
}
.form-control:focus {
  border-color: var(--color-primary);
  box-shadow: 0 0 0 3px rgba(59,130,246,0.1);
  outline: none;
}
```

## 具体执行要点
1. 去掉默认 Bootstrap 紫色导航栏，改为白色顶栏
2. 侧边栏背景改为纯白，激活项用浅蓝底色
3. 主内容区背景用 #F8FAFC（不是纯白）
4. 表格放在白色卡片里，有圆角和细边框
5. 所有圆角统一用 8px 或 12px，不用默认的 4px
6. 去掉多余的 Bootstrap shadow 类，保持扁平
7. 页面顶部加当前半月期的 badge 提示
