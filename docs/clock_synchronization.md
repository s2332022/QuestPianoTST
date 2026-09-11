# Clock synchronization

PC sends at `t0`; Quest timestamps receipt `q1` and response transmission `q2`; PC timestamps receipt `t3`. The sample calculations are:

`RTT = (t3 - t0) - (q2 - q1)`

`Quest-minus-PC offset = ((q1 - t0) + (q2 - t3)) / 2`

Therefore `estimated_pc_timestamp = quest_timestamp - offset`. Initial connection collects at least 10 samples (80 ms spacing); the smallest-RTT sample is selected to reduce queue/asymmetric-delay error. A new sample is requested every 5 seconds. All samples remain in history for later drift regression, but v1 applies only the selected offset.

Logs retain raw Quest time, estimated PC time, and actual PC receive time because offset estimates can be recomputed, transport delay must remain observable, and long-session oscillator drift cannot be recovered from transformed timestamps alone.

