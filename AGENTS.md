# YuoCore AI 开发入口

所有 AI 或自动化开发任务先阅读：

```text
docs/ai-development-guide.md
docs/yuocore-modular-et10-extraction-rules.md
docs/module-dependency-matrix.md
```

`src/YuoCore.Ecs` 内核改动另须先读：

```text
docs/yuocore-ecs-kernel-rules.md
```

硬规则：

- 不把 ASP.NET Core、数据库、Redis、远程网络依赖引入 `YuoCore.Ecs`。
- 不让单机/Unity 最小导入被服务端包污染。
- 不把 Analyzer、Generator、脚手架作为运行时核心依赖。
- ECS 热路径不得新增 `Dictionary<Type, …>` 查找；新派发面必须接入稠密 id 索引（`ComponentTypeRegistry`/`ComponentStore`/`LifecycleIndex`/`_systemsBy*Id`）。
- ECS 内核改动不得改变生命周期派发语义、组件替换语义与派发顺序；宣称的性能收益必须附消融或对照数据。
- 完整吸收一个能力后必须构建、测试、更新文档并本地提交。
- 不推送。
