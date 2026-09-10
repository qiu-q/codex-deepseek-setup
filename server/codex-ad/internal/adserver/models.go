package adserver

import (
	"errors"
	"net/url"
	"strings"
	"time"
	"unicode/utf8"
)

type Advertisement struct {
	Version    int        `json:"version"`
	Enabled    bool       `json:"enabled"`
	CampaignID string     `json:"campaignId"`
	Title      string     `json:"title"`
	Body       string     `json:"body"`
	CTAText    string     `json:"ctaText"`
	TargetURL  string     `json:"targetUrl"`
	ImageURL   string     `json:"imageUrl,omitempty"`
	StartsAt   *time.Time `json:"startsAt"`
	EndsAt     *time.Time `json:"endsAt"`
}

func (ad Advertisement) validate() error {
	if ad.Version != 1 {
		return errors.New("version 必须为 1")
	}
	if strings.TrimSpace(ad.CampaignID) == "" || len([]rune(ad.CampaignID)) > 80 {
		return errors.New("活动编号不能为空且不能超过 80 个字符")
	}
	if strings.TrimSpace(ad.Title) == "" || len([]rune(ad.Title)) > 60 {
		return errors.New("标题不能为空且不能超过 60 个字符")
	}
	if strings.TrimSpace(ad.Body) == "" || len([]rune(ad.Body)) > 180 {
		return errors.New("说明不能为空且不能超过 180 个字符")
	}
	if strings.TrimSpace(ad.CTAText) == "" || len([]rune(ad.CTAText)) > 16 {
		return errors.New("按钮文字不能为空且不能超过 16 个字符")
	}
	if !isHTTPS(ad.TargetURL) {
		return errors.New("点击地址必须是 HTTPS")
	}
	if ad.ImageURL != "" && !isHTTPS(ad.ImageURL) {
		return errors.New("图片地址必须是 HTTPS")
	}
	if ad.StartsAt != nil && ad.EndsAt != nil && !ad.StartsAt.Before(*ad.EndsAt) {
		return errors.New("结束时间必须晚于开始时间")
	}
	return nil
}

func (ad Advertisement) active(now time.Time) bool {
	return ad.Enabled &&
		(ad.StartsAt == nil || !now.Before(*ad.StartsAt)) &&
		(ad.EndsAt == nil || now.Before(*ad.EndsAt))
}

func isHTTPS(raw string) bool {
	parsed, err := url.ParseRequestURI(raw)
	return err == nil && parsed.Scheme == "https" && parsed.Host != ""
}

type CampaignStats struct {
	Impressions uint64 `json:"impressions"`
	Clicks      uint64 `json:"clicks"`
}

type StatsDocument struct {
	Campaigns map[string]CampaignStats `json:"campaigns"`
}

type eventRequest struct {
	CampaignID string `json:"campaignId"`
	Event      string `json:"event"`
}

type OnboardingGuide struct {
	Version int         `json:"version"`
	Enabled bool        `json:"enabled"`
	Title   string      `json:"title"`
	Steps   []GuideStep `json:"steps"`
}

type GuideStep struct {
	ID             string `json:"id"`
	Title          string `json:"title"`
	Body           string `json:"body"`
	CompletionHint string `json:"completionHint"`
	ActionText     string `json:"actionText"`
	ActionURL      string `json:"actionUrl"`
	ImageURL       string `json:"imageUrl,omitempty"`
}

func (guide OnboardingGuide) validate() error {
	if guide.Version != 1 {
		return errors.New("version 必须为 1")
	}
	if !requiredWithin(guide.Title, 80) {
		return errors.New("引导标题不能为空且不能超过 80 个字符")
	}
	if len(guide.Steps) < 1 || len(guide.Steps) > 8 {
		return errors.New("引导步骤必须为 1 到 8 个")
	}
	identifiers := make(map[string]struct{}, len(guide.Steps))
	for _, step := range guide.Steps {
		id := strings.TrimSpace(step.ID)
		if !requiredWithin(id, 48) {
			return errors.New("步骤编号不能为空且不能超过 48 个字符")
		}
		if _, exists := identifiers[id]; exists {
			return errors.New("步骤编号不能重复")
		}
		identifiers[id] = struct{}{}
		if !requiredWithin(step.Title, 60) || !requiredWithin(step.Body, 240) ||
			!requiredWithin(step.CompletionHint, 120) || !requiredWithin(step.ActionText, 16) {
			return errors.New("步骤文字为空或超过长度限制")
		}
		if !isHTTPS(step.ActionURL) {
			return errors.New("步骤操作地址必须是 HTTPS")
		}
		if step.ImageURL != "" && !isHTTPS(step.ImageURL) {
			return errors.New("步骤图片地址必须是 HTTPS")
		}
	}
	return nil
}

func requiredWithin(value string, maximum int) bool {
	return strings.TrimSpace(value) != "" && utf8.RuneCountInString(value) <= maximum
}
