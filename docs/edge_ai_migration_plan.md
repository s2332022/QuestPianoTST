# Edge AI migration plan

Today the PC boundary is `IHandPoseProcessor` with `IdentityHandPoseProcessor`, and `CorrectedPose` uses the same chunked layout as Raw. Phase 2 may add `FutureRuleBasedProcessor` behind that interface and compare latency, joint error, missed notes, loss tolerance, CPU/GPU load, thermals, and battery against Identity.

After a frozen feature/schema baseline, a TCN or GRU may be trained offline, exported to ONNX, numerically validated against its training runtime, and integrated on PC first. A later separately approved `EdgeAiQuest` phase may add Unity Inference Engine on Quest, with ARM64 IL2CPP builds, warm-up, allocation, thermal throttling, battery, end-to-end latency, accuracy, and fallback tests. No model, Inference Engine dependency, TCN, or GRU is included now.

Compare PC distributed inference versus Quest edge inference using identical recorded inputs and metrics: correction accuracy, event-to-display latency/jitter, Wi-Fi loss/outage behavior, clock dependence, sustained frame rate, memory, power/temperature, deployment reproducibility, privacy, and failure recovery. To return to Quest-only MIDI, select `StandaloneQuest`, use the existing standalone scene/Android USB path, and leave all distributed components/scenes unused.

