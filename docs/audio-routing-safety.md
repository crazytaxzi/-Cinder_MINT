# Audio routing safety

Cinder MINT treats every input node as an isolated capture domain.

## Isolation guarantees

- Each input node owns exactly one capture session.
- Split branches receive independent subscriber buffers from that capture session.
- Signals cannot combine inside ordinary processors or output nodes.
- Audible fan-in is allowed only through an explicit Mixer node.
- Sidechain inputs are control-only and are never summed into the audible path.
- The same endpoint cannot be assigned to more than one active input node.
- The same render endpoint cannot be assigned to more than one active output node.

## Loop prevention

Before starting the realtime engine, MINT rejects:

- graph cycles,
- self-cables,
- output-to-input endpoint reuse,
- endpoint-level cycles spanning multiple output nodes,
- duplicate output writers,
- virtual-cable input/output pair reuse when the endpoint family can be identified.

Every output is also wrapped in a runaway feedback guard. If third-party software changes its routing after MINT starts and creates a sustained near-full-scale loop, that output is muted.

A route that must return through an external mixer should use a distinct virtual endpoint in each direction. MINT cannot inspect arbitrary routing performed inside third-party software, so external applications must not send a MINT output back into any MINT input.
