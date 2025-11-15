# Test carrom leaderboard endpoint
# Validates that CarromEventService.GetLeaderboardAsync integrates correctly with backend

# Test total_score metric
GET http://127.0.0.1:8000/game/carrom/leaderboard?metric=total_score&limit=10
HTTP 200
[Asserts]
jsonpath "$.metric" == "total_score"
jsonpath "$.leaderboard" isCollection

# Test total_wins metric
GET http://127.0.0.1:8000/game/carrom/leaderboard?metric=total_wins&limit=5
HTTP 200
[Asserts]
jsonpath "$.metric" == "total_wins"
jsonpath "$.leaderboard" isCollection

# Test with default metric (should use total_score)
GET http://127.0.0.1:8000/game/carrom/leaderboard?limit=10
HTTP 200
[Asserts]
jsonpath "$.metric" == "total_score"
jsonpath "$.leaderboard" isCollection
