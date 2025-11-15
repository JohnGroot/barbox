# Test racing leaderboard endpoint
# Validates that RacingEventService.GetLeaderboardAsync integrates correctly with backend

# Test best_lap metric
GET http://127.0.0.1:8000/game/racing/leaderboard?track_id=gocart_track&metric=best_lap&limit=5
HTTP 200
[Asserts]
jsonpath "$.track_id" == "gocart_track"
jsonpath "$.metric" == "best_lap"
jsonpath "$.leaderboard" isCollection

# Test best_race metric with lap filter
GET http://127.0.0.1:8000/game/racing/leaderboard?track_id=gocart_track&metric=best_race&laps=3&limit=10
HTTP 200
[Asserts]
jsonpath "$.track_id" == "gocart_track"
jsonpath "$.metric" == "best_race"
jsonpath "$.leaderboard" isCollection

# Test best_race metric without lap filter
GET http://127.0.0.1:8000/game/racing/leaderboard?track_id=mountain_circuit&metric=best_race&limit=10
HTTP 200
[Asserts]
jsonpath "$.track_id" == "mountain_circuit"
jsonpath "$.metric" == "best_race"
jsonpath "$.leaderboard" isCollection
