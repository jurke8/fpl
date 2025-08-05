namespace FPL;
public class Data
    {
        public string status { get; set; }
        public int?nowCost { get; set; }
        public int?teamCode { get; set; }
        public PriceInfo? priceInfo { get; set; }
        public int?positionId { get; set; }
        public List<Prediction> predictions { get; set; }
        public string teamCodeName { get; set; }
        public int?next_gw_xmins { get; set; }
        public double prediction4GW { get; set; }
        public string formatted_cost { get; set; }
        public double weighted_prediction { get; set; }
    }

    public class Fpl
    {
        public int?id { get; set; }
        public int?bps { get; set; }
        public int?code { get; set; }
        public string form { get; set; }
        public string news { get; set; }
        public int?team { get; set; }
        public int?bonus { get; set; }
        public string photo { get; set; }
        public int?saves { get; set; }
        public int?starts { get; set; }
        public string status { get; set; }
        public string threat { get; set; }
        public int?assists { get; set; }
        public string ep_next { get; set; }
        public object ep_this { get; set; }
        public int?minutes { get; set; }
        public bool special { get; set; }
        public int?now_cost { get; set; }
        public string web_name { get; set; }
        public int? form_rank { get; set; }
        public string ict_index { get; set; }
        public string influence { get; set; }
        public int?own_goals { get; set; }
        public int?red_cards { get; set; }
        public int?team_code { get; set; }
        public string creativity { get; set; }
        public string first_name { get; set; }
        public DateTime? news_added { get; set; }
        public string value_form { get; set; }
        public string second_name { get; set; }
        public int? threat_rank { get; set; }
        public int?clean_sheets { get; set; }
        public int?element_type { get; set; }
        public int?event_points { get; set; }
        public int?goals_scored { get; set; }
        public bool in_dreamteam { get; set; }
        public double saves_per_90 { get; set; }
        public object squad_number { get; set; }
        public int?total_points { get; set; }
        public int?transfers_in { get; set; }
        public string value_season { get; set; }
        public int?yellow_cards { get; set; }
        public int? now_cost_rank { get; set; }
        public int? selected_rank { get; set; }
        public double starts_per_90 { get; set; }
        public int?transfers_out { get; set; }
        public string expected_goals { get; set; }
        public int? form_rank_type { get; set; }
        public int?goals_conceded { get; set; }
        public int? ict_index_rank { get; set; }
        public int? influence_rank { get; set; }
        public string penalties_text { get; set; }
        public int?creativity_rank { get; set; }
        public int?dreamteam_count { get; set; }
        public int? penalties_order { get; set; }
        public int?penalties_saved { get; set; }
        public string points_per_game { get; set; }
        public string expected_assists { get; set; }
        public int?penalties_missed { get; set; }
        public int? threat_rank_type { get; set; }
        public int?cost_change_event { get; set; }
        public int?cost_change_start { get; set; }
        public int? now_cost_rank_type { get; set; }
        public int? selected_rank_type { get; set; }
        public int?transfers_in_event { get; set; }
        public double clean_sheets_per_90 { get; set; }
        public int?ict_index_rank_type { get; set; }
        public int?influence_rank_type { get; set; }
        public string selected_by_percent { get; set; }
        public int?transfers_out_event { get; set; }
        public int?creativity_rank_type { get; set; }
        public int?points_per_game_rank { get; set; }
        public string direct_freekicks_text { get; set; }
        public double expected_goals_per_90 { get; set; }
        public double goals_conceded_per_90 { get; set; }
        public int?cost_change_event_fall { get; set; }
        public int?cost_change_start_fall { get; set; }
        public int? direct_freekicks_order { get; set; }
        public double expected_assists_per_90 { get; set; }
        public string expected_goals_conceded { get; set; }
        public int?points_per_game_rank_type { get; set; }
        public string expected_goal_involvements { get; set; }
        public int? chance_of_playing_next_round { get; set; }
        public object chance_of_playing_this_round { get; set; }
        public double expected_goals_conceded_per_90 { get; set; }
        public double expected_goal_involvements_per_90 { get; set; }
        public string corners_and_indirect_freekicks_text { get; set; }
        public int? corners_and_indirect_freekicks_order { get; set; }
    }

    public class Live
    {
        public int?bps { get; set; }
        public int?bonus { get; set; }
        public int?saves { get; set; }
        public int?starts { get; set; }
        public string threat { get; set; }
        public int?assists { get; set; }
        public int?minutes { get; set; }
        public string ict_index { get; set; }
        public string influence { get; set; }
        public int?own_goals { get; set; }
        public int?red_cards { get; set; }
        public string creativity { get; set; }
        public int?clean_sheets { get; set; }
        public int?goals_scored { get; set; }
        public bool in_dreamteam { get; set; }
        public int?total_points { get; set; }
        public int?yellow_cards { get; set; }
        public string expected_goals { get; set; }
        public int?goals_conceded { get; set; }
        public int?penalties_saved { get; set; }
        public string expected_assists { get; set; }
        public int?penalties_missed { get; set; }
        public string expected_goals_conceded { get; set; }
        public string expected_goal_involvements { get; set; }
    }

    public class Prediction
    {
        public int?gw { get; set; }
        public List<List<object>> opp { get; set; }
        public int?capt { get; set; }
        public int?xmins { get; set; }
        public string status { get; set; }
        public double fitness { get; set; }
        public double predicted_pts { get; set; }
    }

    public class PriceInfo
    {
        public int?Code { get; set; }
        public string Team { get; set; }
        public double Value { get; set; }
        public int?HrRate { get; set; }
        public string Status { get; set; }
        public int?Target { get; set; }
        public string Position { get; set; }
        public double Ownership { get; set; }
        public string ChangeTime { get; set; }
        public string PlayerName { get; set; }
        public int?RateOfChange { get; set; }
    }

    public class Root
    {
        public int?code { get; set; }
        public string webName { get; set; }
        public string searchTerm { get; set; }
        public Team team { get; set; }
        public int?season { get; set; }
        public Data data { get; set; }
        public Fpl fpl { get; set; }
        public Live live { get; set; }
        public double fpl_ownership { get; set; }
        public int? elite_ownership { get; set; }
        public int? elite_ownership_change { get; set; }
        public string player_pool_status { get; set; }
    }

    public class Team
    {
        public int?code { get; set; }
        public string name { get; set; }
        public string codeName { get; set; }
    }