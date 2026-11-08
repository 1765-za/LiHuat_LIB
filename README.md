# LiHuat_LIB · Linux 分支

**当前为 Linux x86_64 移植开发分支，还不能直接放进游戏使用。**

已增加 Linux 内存/ELF/跳板基础实现、C# 平台加载与诊断 socket 接口，并补回可复现生成的 CLR profiler 回调基类。原有 ABI 11 的完整防护、JIT 接管与运行时挂起仍未完成 Linux 集成。

```sh
make platform   # 生成开发用 libLihuatPlatform.so
make test       # 原生基础功能、ELF 和动态加载测试
make sanitize   # ASan/UBSan/LSan 检查
```

`libLihuatPlatform.so` 是单独的开发支撑库，**不是** `libLihuatNative.so`，不导出 `LihuatNative_Initialize`，不能改名冒充原生后端。C# 的 ABI/authority/启动验证仍然保留，缺少完整后端时会失败，不会假装启用防护。

- [移植范围、测试和剩余工作](docs/linux-port.md)
- [Profiler 缺失输入的恢复及官方来源](docs/profiler-build-inputs.md)

暂以 Linux x86_64 和 RimWorld 1.7/CoreCLR 为目标假设；尚未拿到目标宿主的确切运行时、Harmony 版本和完整托管构建工程，所以没有宣称游戏兼容性。
