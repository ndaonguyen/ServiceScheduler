# PRD: Unified Service Scheduler — Appointment Booking

> _No issue yet — this file will be renamed to `<issue-number>-appointment-booking.md` and back-linked once one is created._

## Problem Statement

Dealership staff currently book vehicle service appointments manually — by phone, spreadsheet, or
whiteboard — checking service bay and technician availability by hand. The process is slow,
error-prone, and produces double-bookings when two staff members schedule against the same bay or
technician at overlapping times. Customers have no self-service way to request an appointment and
get an immediate, reliable confirmation that a bay and a qualified technician are actually
available for the full duration of the service.

## Solution

A signed-in customer can request an appointment for one of their vehicles at a chosen dealership
and service type at a desired start time. The system checks — in real time — whether a bay and a
qualified technician are both free at that dealership for the full length of the service. If yes,
it confirms the booking immediately, assigning a specific bay and technician. If no, it rejects
the request with a clear reason the customer can act on (e.g. "no bay available").

## 1. Assumptions

| ID | Assumption |
|---|---|
| AS-01 | The customer is signed in. Their identity is taken from their session, never from the request itself — a customer can't book on someone else's behalf by supplying another user's id. |
| AS-02 | Vehicles are already linked to their owner elsewhere in the product. Booking consumes that ownership; it doesn't introduce a separate customer profile. |
| AS-03 | A technician is considered busy only when they have another confirmed appointment in this system. Vacation, PTO, and external calendars are out of scope. |
| AS-04 | Every service type has a fixed duration (e.g. "Oil change = 45 minutes"). Variable-length jobs are out of scope. |
| AS-05 | All appointment times are stored in UTC. Showing them in the customer's local time zone is the client app's job. |
| AS-06 | The customer gets a definitive answer — confirmed or rejected — in the same request. No "we'll get back to you" acknowledgements followed by later notifications. This is a hard product requirement: the customer experience assumes an immediate outcome. |

## 2. User Stories

**Customer:**

| ID | Story |
|---|---|
| US-01 | As a customer, I want to request a service appointment for a vehicle at a dealership at a desired start time, so that I don't have to phone the dealership to book. |
| US-02 | As a customer, I want the system to auto-assign an available service bay, so that I don't need to know which bays exist. |
| US-03 | As a customer, I want the system to auto-assign a qualified technician, so that I don't need to know who works there. |
| US-04 | As a customer, I want a confirmed appointment record with the assigned bay, technician, start, and end, so that I know exactly what was booked. |
| US-05 | As a customer, I want a clear reason when a request can't be fulfilled, so that I can retry or adjust. |
| US-06 | As a customer with multiple vehicles, I want each booking to apply to exactly the vehicle I specify, so that I can manage per-vehicle service history. |
| US-07 | As a customer, I want to book at any dealership (not just a home dealership), so that I can get service wherever I currently am. |

**Dealership:**

| ID | Story |
|---|---|
| US-08 | As a dealership, I want the same technician never assigned to overlapping appointments, so that no technician is expected in two places at once. |
| US-09 | As a dealership, I want the same service bay never assigned to overlapping appointments, so that physical resources aren't double-booked. |
| US-10 | As a dealership, I want only technicians who hold the required skill assigned to a service type, so that customers always get a qualified technician. |

## 3. What the System Must Do

| ID | Behaviour |
|---|---|
| FR-01 | Let a signed-in customer submit a booking request specifying: which vehicle, which dealership, which service type, and when they'd like it to start. |
| FR-02 | Always identify the customer from their signed-in session — never trust a customer id supplied in the request. |
| FR-03 | Work out the appointment's end time from the service type's fixed duration. The customer doesn't get to override the length. |
| FR-04 | Automatically pick one service bay at that dealership that's free for the full window. |
| FR-05 | Automatically pick one technician at that dealership who is qualified for that service type and free for the full window. |
| FR-06 | On success, create a confirmed appointment linking the customer, vehicle, dealership, bay, technician, start, and end — and immediately return the confirmation with those details. |
| FR-07 | On failure, return a clear, stable reason the client app can display to the customer (e.g. "no bay available"). |
| FR-08 | Ship the dev environment with a small starter set of dealerships, bays, technicians (with their skills), service types, and customer-owned vehicles, so the flow can be tried out end-to-end without any admin tooling. |

## 4. Business Rules

| ID | Rule |
|---|---|
| BR-01 | A technician cannot have two confirmed appointments that overlap in time. |
| BR-02 | A service bay cannot have two confirmed appointments that overlap in time. |
| BR-03 | Appointments that touch but don't overlap are fine. If one ends at 3:00pm and the next starts at 3:00pm on the same bay or technician, that is _not_ a conflict. |
| BR-04 | A technician can only be assigned to a service type they are qualified for. |
| BR-05 | A service bay can only be assigned to appointments at the dealership it belongs to. |
| BR-06 | A technician can only be assigned to appointments at the dealership they work at. |
| BR-07 | Appointment duration always comes from the service type. Any duration in the customer's request is ignored. |

## 5. Validation Rules

Each row is a reason a booking request will be rejected outright, along with what the customer sees.

| ID | Rule | Customer sees |
|---|---|---|
| VR-01 | The request must come from a signed-in customer. | "You need to sign in." |
| VR-02 | The chosen vehicle must exist. | "We couldn't find that vehicle." |
| VR-03 | The chosen vehicle must belong to the signed-in customer. | "That vehicle isn't yours." |
| VR-04 | The chosen dealership must exist. | "We couldn't find that dealership." |
| VR-05 | The chosen service type must exist. | "We couldn't find that service." |
| VR-06 | The requested start time must be in the future. | "The start time has to be in the future." |

## 6. Quality Requirements

| ID | Requirement |
|---|---|
| NFR-01 | The no-double-booking guarantee (BR-01, BR-02) must hold even under heavy concurrent load — e.g. two customers submitting requests for the same bay in the same second. This is a correctness requirement, not a best-effort one. |
| NFR-02 | Booking reuses the existing sign-in mechanism. No new sign-in flow is introduced. |

## 7. What the Customer Sees

**Success (booking confirmed):**
The customer receives a confirmation containing:
- The dealership name
- The service type name and its duration
- The specific service bay assigned (e.g. "Bay 3")
- The specific technician assigned (name)
- The confirmed start and end times
- Status: **Confirmed**

**Failure (booking rejected):**
The customer receives a short, stable reason. The client app maps each reason to a message. The possible reasons in this slice are:

| Reason | Meaning |
|---|---|
| Start time in the past | The requested start time is not in the future. |
| Vehicle not found | The vehicle id doesn't exist. |
| Vehicle not yours | The vehicle exists but belongs to a different customer. |
| Dealership not found | The dealership id doesn't exist. |
| Service type not found | The service type id doesn't exist. |
| No qualified technician | Either no technician at that dealership can do that service, or every qualified technician is already booked in that window. |
| No bay available | Every bay at that dealership is already booked in that window. |

## 8. How the Flow Works (plain-language walkthrough)

1. The customer submits a booking request: vehicle, dealership, service type, desired start time.
2. The system identifies the customer from their session.
3. The system looks up the service type's duration and works out when the appointment would end.
4. The system checks the start time is in the future — otherwise rejects.
5. The system confirms the vehicle exists and belongs to this customer — otherwise rejects.
6. The system confirms the dealership exists — otherwise rejects.
7. The system fetches the list of technicians at that dealership who are qualified for that service — if the list is empty, rejects with "no qualified technician".
8. The system checks which of that dealership's bays and which of those qualified technicians are free for the full window.
9. If at least one bay _and_ one qualified technician are free, the system picks one of each and creates a confirmed appointment. The confirmation is returned immediately.
10. If nothing is free, the system rejects with either "no bay available" or "no qualified technician", whichever applies.

## 9. What the System Tracks (conceptual)

To support the flow above, the product tracks the following concepts. Attributes are shown at a product level, not a database level.

- **Appointment** — Who the customer is, which vehicle, which dealership, which service type, which bay was assigned, which technician was assigned, when it starts, when it ends, and whether it's confirmed.
- **Vehicle** — Its owner, plus identifying details (make, model, year, VIN).
- **Dealership** — Its name and address.
- **Service Bay** — Which dealership it belongs to, and a label (e.g. "Bay 3").
- **Technician** — Which dealership they work at, and their name.
- **Technician Qualification** — Which service types a technician is allowed to perform.
- **Service Type** — Its name and fixed duration.

## 10. Acceptance Criteria

Each row is a scenario the product must handle correctly.

| ID | Given | When | Then |
|---|---|---|---|
| AT-01 | The vehicle exists and belongs to the customer; the dealership and service type exist; the requested start is in the future; at least one bay and one qualified technician at that dealership are free for the window | The customer submits the booking | Booking is confirmed; a specific bay and technician are assigned; the end time is the start time plus the service type's duration |
| AT-02 | The vehicle id is unknown | The customer submits the booking | Rejected: "vehicle not found" |
| AT-03 | The vehicle exists but belongs to a different customer | The customer submits the booking | Rejected: "vehicle not yours" |
| AT-04 | The dealership id is unknown | The customer submits the booking | Rejected: "dealership not found" |
| AT-05 | The service type id is unknown | The customer submits the booking | Rejected: "service type not found" |
| AT-06 | The requested start time is in the past | The customer submits the booking | Rejected: "start time in the past" |
| AT-07 | The customer is not signed in | The customer submits the booking | Rejected: "you need to sign in" |
| AT-08 | No technician at the dealership can perform this service, OR every qualified technician is busy for the requested window | The customer submits the booking | Rejected: "no qualified technician" |
| AT-09 | Every bay at the dealership is busy for the requested window | The customer submits the booking | Rejected: "no bay available" |
| AT-10 (pins BR-03) | An existing confirmed appointment ends at 3:00pm; the new request starts at 3:00pm on the same bay and technician | The customer submits the booking | Confirmed (back-to-back is fine, not an overlap) |
| AT-11 (pins BR-03) | An existing confirmed appointment covers 3:00pm–3:45pm on a bay; the new request is 2:59:59pm–3:00:01pm on the same bay | The customer submits the booking | Rejected: "no bay available" |
| AT-12 (pins BR-05/06) | Only one bay/technician at the chosen dealership is free; a different dealership has plenty of free resources | The customer submits the booking | Confirmed, using the chosen dealership's resource. Other dealerships' resources are never considered. |
| AT-13 (pins BR-07) | The chosen service type is 45 minutes long; the customer requests a start at 2:00pm | The customer submits the booking | The confirmed end time is 2:45pm — even if the customer's request contained a different duration, it's ignored |

## Future Work

Follow-ups directly triggered or enabled by this slice. Broader product direction (Notifications, Audit, Billing, Reporting, etc.) lives in [`../roadmap.md`](../roadmap.md), not here. These are planned — just not now.

| Item | When it becomes relevant |
|---|---|
| Notify other parts of the product when an appointment is confirmed (e.g. so a future Notifications module can email the customer). | When the first consumer of "appointment confirmed" events exists — expected to be the Notifications module on the roadmap. |
| Add a stronger test harness that verifies the no-double-booking guarantee under real database conditions. | When we need to prove concurrent double-booking is prevented end-to-end, not only in isolated logic tests. |
| Add build-time checks that keep modules isolated from each other as the codebase grows. | Once a second module is under active development. |

## Out of Scope

Items this PRD deliberately does **not** cover and has no committed plan to add.

- Admin tooling to create or edit vehicles, dealerships, bays, technicians, qualifications, or service types (created via seeded starter data only in this slice).
- A separate customer profile — the customer is just the vehicle's owner.
- Cancelling, rescheduling, viewing, or listing appointments. This slice only creates them.
- Letting the customer pick a specific bay or technician. Bay and technician are always chosen for them.
- Rate limiting or anti-abuse controls on the booking endpoint.
- Displaying times in the customer's local time zone. All times are UTC end-to-end; local display is a client-app concern.

## Further Notes

- Wide blast radius: this slice introduces four areas of the product (booking, vehicles/dealerships/bays, technicians/qualifications, service types) at once. That's unavoidable — a booking can't be exercised end-to-end without all four existing first, and no prior slice created them.
- No GitHub issue exists yet. Rename this file to `<issue-number>-appointment-booking.md` when one is created.
