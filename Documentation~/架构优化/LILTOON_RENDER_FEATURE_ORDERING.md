# RendererFeature 顺序契约

状态：HoUrp17.3.0 fork 已实现，待 Unity 实机回归。

## 结论

RendererFeature 的顺序由两级键决定：

1. `RenderPassEvent` 决定管线大阶段；
2. 同一事件内，`ScriptableRenderer.EnqueuePass` 的调用序号决定先后。

Renderer Feature 列表顺序会决定 `AddRenderPasses` 的调用顺序，因此会直接决定同事件 pass 的入队序号。未来插入一个 Feature 时，应通过调整 Renderer Data 的列表位置表达依赖，不使用 `250 + 1` 这类隐藏事件偏移。

## HoUrp 实现

HoUrp 的 `ScriptableRenderer` 在每帧开始清空 active pass queue，并给每次 `EnqueuePass` 分配 `renderPassEnqueueOrder`。排序键为：

```text
(pass.renderPassEvent, pass.renderPassEnqueueOrder)
```

这保留了 URP 原有的大阶段排序，同时把同事件的列表顺序变成显式代码契约。该序号是每帧重新分配的，不跨帧保存，也不影响 NativeRenderPass 的 `renderPassQueueIndex`。

## 资源依赖规则

同事件顺序只保证 RenderGraph 的记录顺序。真正的 GPU 依赖仍必须声明：

- 生产者：`SetRenderAttachment` / `SetGlobalTextureAfterPass`；
- 消费者：`UseTexture` 或明确的全局纹理声明；
- 跨 Feature 的资源不得依赖未声明的全局状态。

如果消费者在生产者之前入队，资源句柄可能在 `RecordRenderGraph` 时无效；应修正 Renderer Feature 列表顺序，而不是把消费者改到下一个事件。

## 已核对的事实

- HoUrp `SortStable` 使用插入排序，原本已经具有稳定性，但原实现仅比较 `RenderPassEvent`，同事件顺序依赖隐式稳定性。
- HoUrp RenderGraph 按事件区间调用 `RecordRenderGraph`，并不会为跨 Feature 的资源自动推断“谁先创建 ContextItem”。
- Unity 官方 RenderGraph 示例要求每个 pass 显式声明读取/写入的资源，RenderGraph 据此建立依赖和优化；Renderer Feature 的 `EnqueuePass` 只是把 pass 放入队列。

## 验收

1. Renderer Data 中 GeometryBuffer 在 Ho-GTAO 上方，二者使用同一命名事件；Frame Debugger/Render Graph Viewer 中 GeometryBuffer 必须先出现。
2. 在两者中间插入一个同事件 Feature，该 Feature 必须出现在二者之间，不需要修改任何数值事件。
3. 移动列表顺序后，执行顺序随之改变；不同事件仍按事件值优先。
4. GeometryBuffer 未执行时，Ho-GTAO 应明确报告缺少输入并降级白色 AO，不得读取上一帧全局纹理。
