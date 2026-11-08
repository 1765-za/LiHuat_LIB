# LiHuat_LIB · Linux 分支

注:我没有Linux开发经验,Linux分支是纯AI



**当前为 Linux x86_64 移植开发分支，还不能直接放进游戏使用。**

已增加 Linux 内存/ELF/跳板基础实现、经过 backing-file 验证的共享代码映射、C# 平台加载与诊断 socket 接口，并恢复 net10.0 构建工程。独立 CLR profiler 支撑模块已实现生命周期与运行时挂起接口，但原有 ABI 11 的完整防护和 JIT 接管仍未完成 Linux 集成。

```sh
make platform   # 生成开发用 libLihuatPlatform.so
make test       # 原生基础功能、ELF 和动态加载测试
make sanitize   # ASan/UBSan/LSan 检查
```

`libLihuatPlatform.so` 是单独的开发支撑库，**不是** `libLihuatNative.so`，不导出 `LihuatNative_Initialize`，不能改名冒充原生后端。C# 的 ABI/authority/启动验证仍然保留，缺少完整后端时会失败，不会假装启用防护。

- [移植范围、测试和剩余工作](docs/linux-port.md)
- [Profiler 缺失输入的恢复及官方来源](docs/profiler-build-inputs.md)
- [Linux profiler 支撑模块](docs/linux-profiler.md)
- [托管构建配置与所需真实引用](docs/managed-build.md)

暂以 Linux x86_64 和 RimWorld 1.7/CoreCLR 为目标假设；尚未拿到目标宿主的确切运行时、Harmony 版本及真实引用程序集，所以没有宣称游戏兼容性。
