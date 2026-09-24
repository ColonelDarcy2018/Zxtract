param(
    [string]$Endpoint = "http://127.0.0.1:7600",
    [string]$DocumentId = "tab-1",
    [string]$PageId = "0:5"
)

$ErrorActionPreference = "Stop"

function ConvertTo-JsxText {
    param([string]$Value)
    return [System.Security.SecurityElement]::Escape($Value)
}

function Invoke-OpenPencilTool {
    param(
        [string]$Name,
        [hashtable]$Arguments,
        [string]$TargetPageId = $PageId
    )

    $request = @{
        command = "tool"
        args = @{
            document_id = $DocumentId
            page_id = $TargetPageId
            name = $Name
            args = $Arguments
        }
    }
    $json = $request | ConvertTo-Json -Depth 80 -Compress
    try {
        return Invoke-RestMethod -Uri "$Endpoint/rpc" -Method Post `
            -ContentType "application/json; charset=utf-8" `
            -Body ([Text.Encoding]::UTF8.GetBytes($json))
    }
    catch {
        # Large render calls can complete in the app just after the bridge's
        # 20-second response timeout. Leave time for the document transaction.
        if ($_.ErrorDetails.Message -match "RPC timeout") {
            Start-Sleep -Seconds 3
            return $null
        }
        throw
    }
}

function New-ButtonJsx {
    param(
        [string]$Label,
        [ValidateSet("primary", "secondary", "text")]
        [string]$Kind = "secondary",
        [int]$Width = 0
    )
    $safe = ConvertTo-JsxText $Label
    $widthAttr = if ($Width -gt 0) { " w={$Width}" } else { "" }
    if ($Kind -eq "primary") {
        return "<Frame name=`"Button / Primary`" flex=`"row`" items=`"center`" justify=`"center`"$widthAttr h={34} px={16} bg=`"#2F6FED`" rounded={5}><Text size={13} weight={600} color=`"#FFFFFF`">$safe</Text></Frame>"
    }
    if ($Kind -eq "text") {
        return "<Frame name=`"Button / Text`" flex=`"row`" items=`"center`" justify=`"center`"$widthAttr h={34} px={8}><Text size={13} weight={600} color=`"#2F6FED`">$safe</Text></Frame>"
    }
    return "<Frame name=`"Button / Secondary`" flex=`"row`" items=`"center`" justify=`"center`"$widthAttr h={34} px={14} bg=`"#FFFFFF`" stroke=`"#CFD5DD`" strokeWidth={1} rounded={5}><Text size={13} weight={600} color=`"#273142`">$safe</Text></Frame>"
}

function New-TaskRowJsx {
    param(
        [string]$Name,
        [string]$Path,
        [string]$Status,
        [string]$Detail,
        [string]$Action,
        [string]$StatusColor = "#5F6877",
        [int]$Progress = -1
    )
    $safeName = ConvertTo-JsxText $Name
    $safePath = ConvertTo-JsxText $Path
    $safeStatus = ConvertTo-JsxText $Status
    $safeDetail = ConvertTo-JsxText $Detail
    $safeAction = ConvertTo-JsxText $Action
    $progressJsx = ""
    if ($Progress -ge 0) {
        $barWidth = [Math]::Max(2, [Math]::Round(220 * $Progress / 100))
        $progressJsx = "<Frame name=`"Progress Track`" w={220} h={3} bg=`"#E4E8ED`" rounded={2}><Rectangle name=`"Progress`" w={$barWidth} h={3} bg=`"#2F6FED`" rounded={2} /></Frame>"
    }
    return @"
<Frame name="Task / $safeName" w="fill" h={64} flex="row" items="center" px={24} bg="#FFFFFF" stroke="#E2E6EB" strokeWidth={1}>
  <Frame name="File" w="fill" flex="col" gap={3}>
    <Text size={14} weight={600} color="#202938">$safeName</Text>
    <Text size={11} color="#808998">$safePath</Text>
  </Frame>
  <Frame name="Status" w={280} flex="col" gap={4}>
    <Text size={13} weight={600} color="$StatusColor">$safeStatus</Text>
    <Text size={11} color="#808998">$safeDetail</Text>
    $progressJsx
  </Frame>
  <Frame name="Actions" w={230} flex="row" items="center" gap={12}>
    <Text size={13} weight={600} color="#2F6FED">$safeAction</Text>
    <Text size={13} color="#697386">复制路径</Text>
  </Frame>
</Frame>
"@
}

function New-ScreenJsx {
    param(
        [string]$Name,
        [ValidateSet("files", "folder")]
        [string]$Mode,
        [ValidateSet("ready", "running", "completed", "paused", "attention", "empty")]
        [string]$State,
        [string]$OverallStatus,
        [array]$Rows,
        [string]$RootDirectory = "D:\资料\待整理"
    )

    $safeName = ConvertTo-JsxText $Name
    $safeOverall = ConvertTo-JsxText $OverallStatus
    $safeRoot = ConvertTo-JsxText $RootDirectory
    $fileActiveBg = if ($Mode -eq "files") { "#EAF1FF" } else { "#FFFFFF" }
    $fileActiveColor = if ($Mode -eq "files") { "#245EC7" } else { "#687384" }
    $folderActiveBg = if ($Mode -eq "folder") { "#EAF1FF" } else { "#FFFFFF" }
    $folderActiveColor = if ($Mode -eq "folder") { "#245EC7" } else { "#687384" }

    if ($State -eq "running") {
        $rightActions = "$(New-ButtonJsx -Label '暂停' -Kind secondary -Width 72)$(New-ButtonJsx -Label '取消' -Kind secondary -Width 72)"
    }
    elseif ($State -eq "paused") {
        $rightActions = "$(New-ButtonJsx -Label '继续' -Kind primary -Width 72)$(New-ButtonJsx -Label '取消' -Kind secondary -Width 72)"
    }
    elseif ($Mode -eq "folder") {
        $rightActions = "$(New-ButtonJsx -Label '选择文件夹' -Kind secondary -Width 100)$(New-ButtonJsx -Label '扫描并解压' -Kind primary -Width 122)"
    }
    else {
        $rightActions = "$(New-ButtonJsx -Label '＋ 添加文件' -Kind secondary -Width 104)$(New-ButtonJsx -Label '开始解压' -Kind primary -Width 104)"
    }

    $sourceRow = ""
    if ($Mode -eq "folder") {
        $sourceRow = @"
<Frame name="Folder Source" w="fill" h={48} flex="row" items="center" px={24} gap={16} bg="#FFFFFF" stroke="#E2E6EB" strokeWidth={1}>
  <Text size={12} color="#808998">当前目录</Text>
  <Text size={13} color="#273142" grow={1}>$safeRoot</Text>
  <Text size={12} color="#808998">包含子文件夹</Text>
  <Text size={13} weight={600} color="#2F6FED">仅扫描</Text>
</Frame>
"@
    }

    $filterAll = if ($State -eq "attention") { "全部 6" } elseif ($Rows.Count -gt 0) { "全部 $($Rows.Count)" } else { "全部 0" }
    $runningCount = if ($State -eq "running") { "处理中 1" } else { "处理中 0" }
    $attentionCount = if ($State -eq "attention") { "需处理 3" } elseif ($State -eq "completed") { "需处理 1" } else { "需处理 0" }
    $completeCount = if ($State -eq "completed") { "已完成 2" } elseif ($State -eq "attention") { "已完成 3" } else { "已完成 0" }

    $rowJsx = ""
    foreach ($row in $Rows) {
        $rowJsx += New-TaskRowJsx @row
    }
    if ($Rows.Count -eq 0) {
        $emptyTitle = if ($Mode -eq "folder") { "选择文件夹，扫描其中的压缩文件" } else { "拖入压缩文件，从这里开始" }
        $emptyHint = if ($Mode -eq "folder") { "支持递归扫描、嵌套压缩包与跨目录分卷" } else { "支持 ZIP、7Z、RAR 和分卷文件" }
        $rowJsx = @"
<Frame name="Empty State" w="fill" h="fill" flex="col" items="center" justify="center" gap={8} bg="#FFFFFF">
  <Text size={18} weight={600} color="#202938">$emptyTitle</Text>
  <Text size={13} color="#808998">$emptyHint</Text>
</Frame>
"@
    }

    return @"
<Frame name="$safeName" w={1200} h={760} flex="col" bg="#F7F8FA" stroke="#D8DDE4" strokeWidth={1} rounded={8} overflow="hidden" shadow="0 8 24 #00000018">
  <Frame name="Toolbar" w="fill" h={64} flex="row" items="center" px={24} gap={8} bg="#FFFFFF" stroke="#E2E6EB" strokeWidth={1}>
    <Frame name="Workflow Tabs" flex="row" gap={4}>
      <Frame name="Tab / Files" h={36} px={16} flex="row" items="center" bg="$fileActiveBg" rounded={5}><Text size={13} weight={600} color="$fileActiveColor">文件解压</Text></Frame>
      <Frame name="Tab / Folder" h={36} px={16} flex="row" items="center" bg="$folderActiveBg" rounded={5}><Text size={13} weight={600} color="$folderActiveColor">文件夹解压</Text></Frame>
    </Frame>
    <Text name="Overall Status" size={12} color="#697386" grow={1}>$safeOverall</Text>
    <Frame name="Primary Actions" flex="row" items="center" gap={8}>$rightActions</Frame>
  </Frame>
  $sourceRow
  <Frame name="Common Settings" w="fill" h={52} flex="row" items="center" px={24} gap={12} bg="#F7F8FA">
    <Text size={12} color="#808998">密码</Text>
    <Frame h={30} px={10} flex="row" items="center" bg="#FFFFFF" stroke="#CFD5DD" strokeWidth={1} rounded={5}><Text size={12} color="#273142">已设 2 个候选</Text></Frame>
    <Rectangle w={1} h={20} bg="#D8DDE4" />
    <Text size={12} color="#808998">输出</Text>
    <Text size={13} color="#273142" grow={1}>压缩包同名文件夹</Text>
    <Frame h={30} px={10} flex="row" items="center" bg="#FFFFFF" stroke="#CFD5DD" strokeWidth={1} rounded={5}><Text size={12} color="#273142">更多选项</Text></Frame>
  </Frame>
  <Frame name="Filters" w="fill" h={64} flex="row" items="center" px={24} gap={20} bg="#FFFFFF" stroke="#E2E6EB" strokeWidth={1}>
    <Text size={13} weight={600} color="#245EC7">$filterAll</Text>
    <Text size={13} color="#697386">$runningCount</Text>
    <Text size={13} color="#697386">$attentionCount</Text>
    <Text size={13} color="#697386">$completeCount</Text>
    <Frame grow={1} h={1} />
    <Frame name="Search" w={220} h={32} flex="row" items="center" px={10} bg="#FFFFFF" stroke="#CFD5DD" strokeWidth={1} rounded={5}><Text size={12} color="#929AA7">搜索文件</Text></Frame>
  </Frame>
  <Frame name="Task List" w="fill" h="fill" flex="col" bg="#FFFFFF">
    <Frame name="Table Header" w="fill" h={38} flex="row" items="center" px={24} bg="#FAFBFC" stroke="#E2E6EB" strokeWidth={1}>
      <Text size={11} weight={600} color="#808998" grow={1}>文件</Text>
      <Text size={11} weight={600} color="#808998" w={280}>状态 / 进度</Text>
      <Text size={11} weight={600} color="#808998" w={230}>操作</Text>
    </Frame>
    $rowJsx
  </Frame>
  <Frame name="Status Bar" w="fill" h={40} flex="row" items="center" gap={12} px={24} bg="#F7F8FA" stroke="#E2E6EB" strokeWidth={1}>
    <Text size={12} color="#5F6877" grow={1}>$safeOverall</Text>
    <Text size={12} weight={600} color="#2F6FED">查看日志</Text>
    <Text size={12} color="#697386">导出</Text>
  </Frame>
</Frame>
"@
}

function Add-DesignFrame {
    param([string]$Jsx, [int]$X, [int]$Y)
    Invoke-OpenPencilTool -Name "render" -Arguments @{ x = $X; y = $Y; jsx = $Jsx } | Out-Null
}

$filesReady = @(
    @{ Name = "资料合集.7z"; Path = "D:\资料\下载"; Status = "等待开始"; Detail = "342 MB"; Action = "移出" },
    @{ Name = "课程.part01.rar"; Path = "4 个分卷 · 跨目录"; Status = "等待开始"; Detail = "1.8 GB"; Action = "移出" },
    @{ Name = "图片.zip"; Path = "D:\图片\归档"; Status = "等待开始"; Detail = "86 MB"; Action = "移出" }
)
$filesRunning = @(
    @{ Name = "资料合集.7z"; Path = "D:\资料\下载"; Status = "正在解压 46%"; Detail = "已处理 158 MB"; Action = "详情"; StatusColor = "#245EC7"; Progress = 46 },
    @{ Name = "课程.part01.rar"; Path = "4 个分卷 · 跨目录"; Status = "等待中"; Detail = "前面还有 1 项"; Action = "详情" },
    @{ Name = "图片.zip"; Path = "D:\图片\归档"; Status = "等待中"; Detail = "前面还有 2 项"; Action = "详情" }
)
$filesCompleted = @(
    @{ Name = "资料合集.7z"; Path = "D:\资料\下载"; Status = "已完成"; Detail = "用时 00:38"; Action = "打开位置"; StatusColor = "#218A54" },
    @{ Name = "课程.part01.rar"; Path = "4 个分卷 · 跨目录"; Status = "密码不匹配"; Detail = "已尝试 3 个候选密码"; Action = "修改密码"; StatusColor = "#C2413B" },
    @{ Name = "图片.zip"; Path = "D:\图片\归档"; Status = "已完成"; Detail = "用时 00:12"; Action = "打开位置"; StatusColor = "#218A54" }
)
$folderRunning = @(
    @{ Name = "素材包.7z.001"; Path = "3 个分卷 · 已归并"; Status = "正在解压 63%"; Detail = "递归轮次 1 / 5"; Action = "详情"; StatusColor = "#245EC7"; Progress = 63 },
    @{ Name = "内层资源.zip"; Path = "由上一项解压产生"; Status = "等待中"; Detail = "将在下一轮检测"; Action = "详情" },
    @{ Name = "文档.rar"; Path = "D:\资料\待整理\文档"; Status = "等待中"; Detail = "已识别密码规则"; Action = "详情" }
)
$attentionRows = @(
    @{ Name = "课程资料.rar"; Path = "D:\课程\解压密码：示例"; Status = "密码不匹配"; Detail = "已尝试 3 个候选密码"; Action = "修改密码"; StatusColor = "#C2413B" },
    @{ Name = "图标合集.7z.001"; Path = "D:\素材\图标"; Status = "缺少分卷"; Detail = "未找到 .003"; Action = "查看分卷"; StatusColor = "#B26A00" },
    @{ Name = "项目备份.zip"; Path = "D:\备份\旧项目"; Status = "解压失败"; Detail = "文件尾部数据异常"; Action = "查看原因"; StatusColor = "#C2413B" }
)

$screens = @(
    @{ X = 0; Y = 320; Jsx = New-ScreenJsx -Name "01 · 文件解压 · 待开始" -Mode files -State ready -OverallStatus "3 项等待开始" -Rows $filesReady },
    @{ X = 1280; Y = 320; Jsx = New-ScreenJsx -Name "02 · 文件解压 · 处理中" -Mode files -State running -OverallStatus "正在处理 1 / 3" -Rows $filesRunning },
    @{ X = 2560; Y = 320; Jsx = New-ScreenJsx -Name "03 · 文件解压 · 已完成" -Mode files -State completed -OverallStatus "已完成 2 / 3 · 1 项需处理" -Rows $filesCompleted },
    @{ X = 0; Y = 1160; Jsx = New-ScreenJsx -Name "04 · 文件夹解压 · 待扫描" -Mode folder -State empty -OverallStatus "尚未扫描" -Rows @() },
    @{ X = 1280; Y = 1160; Jsx = New-ScreenJsx -Name "05 · 文件夹解压 · 处理中" -Mode folder -State running -OverallStatus "扫描到 6 个压缩包 · 正在处理第 1 轮" -Rows $folderRunning },
    @{ X = 2560; Y = 1160; Jsx = New-ScreenJsx -Name "06 · 文件夹解压 · 已暂停" -Mode folder -State paused -OverallStatus "已暂停 · 当前任务将在继续后恢复" -Rows $folderRunning },
    @{ X = 0; Y = 2000; Jsx = New-ScreenJsx -Name "07 · 文件夹解压 · 需处理" -Mode folder -State attention -OverallStatus "已完成 3 / 6 · 3 项需处理" -Rows $attentionRows }
)

foreach ($screen in $screens) {
    Add-DesignFrame -Jsx $screen.Jsx -X $screen.X -Y $screen.Y
}

$compact = (New-ScreenJsx -Name "08 · 紧凑窗口 1024 × 640" -Mode files -State completed -OverallStatus "已完成 2 / 3 · 1 项需处理" -Rows $filesCompleted) `
    -replace 'w=\{1200\} h=\{760\}', 'w={1024} h={640}' `
    -replace 'w=\{280\}', 'w={220}' `
    -replace 'w=\{230\}', 'w={170}'
Add-DesignFrame -Jsx $compact -X 1280 -Y 2000

$passwordPanel = @'
<Frame name="09 · 共用密码" w={820} h={560} flex="col" gap={18} p={24} bg="#FFFFFF" stroke="#D8DDE4" strokeWidth={1} rounded={8} shadow="0 10 28 #00000020">
  <Frame w="fill" flex="row" items="center"><Text size={18} weight={700} color="#202938" grow={1}>本次解压密码</Text><Text size={18} color="#808998">×</Text></Frame>
  <Text size={12} color="#697386">所有候选密码按顺序尝试；运行中的任务不会被修改。</Text>
  <Frame w="fill" h="fill" flex="row" gap={24}>
    <Frame w="fill" flex="col" gap={8}><Text size={13} weight={600} color="#273142">候选密码</Text><Text size={11} color="#808998">每行一个，按顺序尝试</Text><Frame w="fill" h={150} p={12} bg="#FFFFFF" stroke="#CFD5DD" strokeWidth={1} rounded={5}><Text size={13} lineHeight={24} color="#273142">••••••••&#10;••••••&#10;••••••••••</Text></Frame><Frame flex="row" gap={8}><Frame h={32} px={12} flex="row" items="center" bg="#FFFFFF" stroke="#CFD5DD" strokeWidth={1} rounded={5}><Text size={12} color="#273142">保存到密码库</Text></Frame><Text size={12} color="#808998">仅在明确保存时记忆</Text></Frame></Frame>
    <Frame w={310} flex="col" gap={12}><Text size={13} weight={600} color="#273142">快速复用</Text><Frame flex="row" gap={8} items="center"><Rectangle w={16} h={16} bg="#2F6FED" rounded={3}/><Text size={12} color="#273142">从文件名和路径推断密码</Text></Frame><Frame flex="row" wrap={true} gap={8}><Frame h={30} px={10} flex="row" items="center" bg="#F7F8FA" rounded={5}><Text size={12} color="#273142">课程资料 · ••••••</Text></Frame><Frame h={30} px={10} flex="row" items="center" bg="#F7F8FA" rounded={5}><Text size={12} color="#273142">常用 · ••••••••</Text></Frame></Frame><Text size={12} weight={600} color="#2F6FED">管理密码与规则</Text></Frame>
  </Frame>
  <Frame w="fill" flex="row" justify="end" gap={8}><Frame h={34} px={16} flex="row" items="center" bg="#FFFFFF" stroke="#CFD5DD" strokeWidth={1} rounded={5}><Text size={13} color="#273142">取消</Text></Frame><Frame h={34} px={16} flex="row" items="center" bg="#2F6FED" rounded={5}><Text size={13} weight={600} color="#FFFFFF">应用到下次任务</Text></Frame></Frame>
</Frame>
'@
Add-DesignFrame -Jsx $passwordPanel -X 0 -Y 2860

$retryPanel = @'
<Frame name="10 · 修改密码并重试" w={820} h={560} flex="col" gap={18} p={24} bg="#FFFFFF" stroke="#D8DDE4" strokeWidth={1} rounded={8} shadow="0 10 28 #00000020">
  <Frame w="fill" flex="row" items="center"><Text size={18} weight={700} color="#202938" grow={1}>修改密码并重试</Text><Text size={18} color="#808998">×</Text></Frame>
  <Frame w="fill" p={14} flex="col" gap={5} bg="#FFF6F5" stroke="#F2C9C6" strokeWidth={1} rounded={6}><Text size={14} weight={600} color="#9E332E">课程资料.rar</Text><Text size={12} color="#7D5552">上次尝试的 3 个候选密码均不匹配；来源路径和分卷集合已保留。</Text></Frame>
  <Text size={13} weight={600} color="#273142">为本次重试覆盖候选密码</Text>
  <Frame w="fill" h={150} p={12} bg="#FFFFFF" stroke="#CFD5DD" strokeWidth={1} rounded={5}><Text size={13} lineHeight={24} color="#273142">输入新密码，或从密码库选择&#10;••••••••</Text></Frame>
  <Frame flex="row" wrap={true} gap={8}><Frame h={30} px={10} flex="row" items="center" bg="#F7F8FA" rounded={5}><Text size={12} color="#273142">课程资料 · ••••••</Text></Frame><Frame h={30} px={10} flex="row" items="center" bg="#F7F8FA" rounded={5}><Text size={12} color="#273142">从路径重新推断</Text></Frame></Frame>
  <Frame grow={1} />
  <Frame w="fill" flex="row" justify="end" gap={8}><Frame h={34} px={16} flex="row" items="center" bg="#FFFFFF" stroke="#CFD5DD" strokeWidth={1} rounded={5}><Text size={13} color="#273142">取消</Text></Frame><Frame h={34} px={16} flex="row" items="center" bg="#2F6FED" rounded={5}><Text size={13} weight={600} color="#FFFFFF">应用并重试</Text></Frame></Frame>
</Frame>
'@
Add-DesignFrame -Jsx $retryPanel -X 900 -Y 2860

$detailsPanel = @'
<Frame name="11 · 分卷任务详情" w={820} h={560} flex="col" gap={16} p={24} bg="#FFFFFF" stroke="#D8DDE4" strokeWidth={1} rounded={8} shadow="0 10 28 #00000020">
  <Frame w="fill" flex="row" items="center"><Text size={18} weight={700} color="#202938" grow={1}>分卷任务详情</Text><Text size={18} color="#808998">×</Text></Frame>
  <Frame w="fill" flex="col" gap={5}><Text size={14} weight={600} color="#273142">图标合集.7z.001</Text><Text size={12} color="#697386">跨目录分卷 · 已发现 2 / 3</Text></Frame>
  <Frame w="fill" flex="col" gap={8}>
    <Frame w="fill" h={48} flex="row" items="center" px={12} bg="#F7F8FA" rounded={5}><Text size={13} color="#273142" grow={1}>图标合集.7z.001</Text><Text size={12} color="#218A54">已找到</Text></Frame>
    <Frame w="fill" h={48} flex="row" items="center" px={12} bg="#F7F8FA" rounded={5}><Text size={13} color="#273142" grow={1}>图标合集.7z.002</Text><Text size={12} color="#218A54">已找到</Text></Frame>
    <Frame w="fill" h={48} flex="row" items="center" px={12} bg="#FFF8ED" stroke="#F2D29A" strokeWidth={1} rounded={5}><Text size={13} color="#273142" grow={1}>图标合集.7z.003</Text><Text size={12} color="#B26A00">缺失</Text></Frame>
  </Frame>
  <Frame w="fill" p={12} bg="#F7F8FA" rounded={5}><Text size={12} lineHeight={20} color="#697386">将缺失分卷放回扫描目录后，可直接点击“重新检测并重试”，无需重新选择根目录。</Text></Frame>
  <Frame grow={1} />
  <Frame w="fill" flex="row" justify="end" gap={8}><Frame h={34} px={16} flex="row" items="center" bg="#FFFFFF" stroke="#CFD5DD" strokeWidth={1} rounded={5}><Text size={13} color="#273142">复制来源路径</Text></Frame><Frame h={34} px={16} flex="row" items="center" bg="#2F6FED" rounded={5}><Text size={13} weight={600} color="#FFFFFF">重新检测并重试</Text></Frame></Frame>
</Frame>
'@
Add-DesignFrame -Jsx $detailsPanel -X 1800 -Y 2860

$optionsPanel = @'
<Frame name="12 · 更多选项" w={820} h={500} flex="col" gap={18} p={24} bg="#FFFFFF" stroke="#D8DDE4" strokeWidth={1} rounded={8} shadow="0 10 28 #00000020">
  <Frame w="fill" flex="row" items="center"><Text size={18} weight={700} color="#202938" grow={1}>更多选项</Text><Text size={18} color="#808998">×</Text></Frame>
  <Frame w="fill" flex="row" gap={28}>
    <Frame w="fill" flex="col" gap={10}><Text size={13} weight={600} color="#273142">输出位置</Text><Frame h={34} px={12} flex="row" items="center" bg="#EAF1FF" stroke="#2F6FED" strokeWidth={1} rounded={5}><Text size={12} color="#245EC7">同名子文件夹</Text></Frame><Frame h={34} px={12} flex="row" items="center" bg="#FFFFFF" stroke="#CFD5DD" strokeWidth={1} rounded={5}><Text size={12} color="#273142">压缩包所在目录</Text></Frame><Frame h={34} px={12} flex="row" items="center" bg="#FFFFFF" stroke="#CFD5DD" strokeWidth={1} rounded={5}><Text size={12} color="#273142">指定目录</Text></Frame></Frame>
    <Frame w="fill" flex="col" gap={12}><Text size={13} weight={600} color="#273142">冲突与并行</Text><Frame flex="row" items="center"><Text size={12} color="#697386" grow={1}>文件冲突</Text><Text size={12} color="#273142">自动重命名</Text></Frame><Frame flex="row" items="center"><Text size={12} color="#697386" grow={1}>并行任务</Text><Frame w={54} h={30} flex="row" items="center" justify="center" bg="#FFFFFF" stroke="#CFD5DD" strokeWidth={1} rounded={5}><Text size={12} color="#273142">2</Text></Frame></Frame><Frame flex="row" gap={8} items="center"><Rectangle w={16} h={16} bg="#FFFFFF" stroke="#AAB2BE" strokeWidth={1} rounded={3}/><Text size={12} color="#C2413B">成功后删除源压缩文件</Text></Frame></Frame>
    <Frame w="fill" flex="col" gap={12}><Text size={13} weight={600} color="#273142">文件夹模式</Text><Frame flex="row" gap={8} items="center"><Rectangle w={16} h={16} bg="#2F6FED" rounded={3}/><Text size={12} color="#273142">递归解压嵌套压缩包</Text></Frame><Frame flex="row" gap={8} items="center"><Rectangle w={16} h={16} bg="#2F6FED" rounded={3}/><Text size={12} color="#273142">跨目录归并分卷</Text></Frame><Frame flex="row" items="center"><Text size={12} color="#697386" grow={1}>最大递归轮次</Text><Text size={12} color="#273142">5</Text></Frame></Frame>
  </Frame>
  <Frame grow={1} />
  <Frame w="fill" flex="row" justify="end" gap={8}><Frame h={34} px={16} flex="row" items="center" bg="#FFFFFF" stroke="#CFD5DD" strokeWidth={1} rounded={5}><Text size={13} color="#273142">恢复默认</Text></Frame><Frame h={34} px={16} flex="row" items="center" bg="#2F6FED" rounded={5}><Text size={13} weight={600} color="#FFFFFF">完成</Text></Frame></Frame>
</Frame>
'@
Add-DesignFrame -Jsx $optionsPanel -X 2700 -Y 2860

$tree = Invoke-OpenPencilTool -Name "get_page_tree" -Arguments @{ depth = 1 }
$tree
